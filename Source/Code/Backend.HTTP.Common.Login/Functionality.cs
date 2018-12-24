/*
 * Copyright 2017 Stanislav Muhametsin. All rights Reserved.
 *
 * Licensed  under the  Apache License,  Version 2.0  (the "License");
 * you may not use  this file  except in  compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed  under the  License is distributed on an "AS IS" BASIS,
 * WITHOUT  WARRANTIES OR CONDITIONS  OF ANY KIND, either  express  or
 * implied.
 *
 * See the License for the specific language governing permissions and
 * limitations under the License. 
 */
using Backend.Core;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UtilPack;
using UtilPack.Configuration;

namespace Backend.HTTP.Common.Login
{
   [ConfigurationType( typeof( LoginConfiguration ) )]
   public class LoginFunctionalityFactory : PathBasedRegexMatchingResponseCreatorFactory
   {
      private readonly LoginConfiguration _config;
      public LoginFunctionalityFactory(
         LoginConfiguration loginConfiguration
         ) : base( loginConfiguration )
      {
         this._config = loginConfiguration;
      }

      protected override Task<ResponseCreator<HttpRequest, HttpContext>> DoCreateResponseCreatorAsync(
         ResponseCreatorInstantiationParameters creationParameters,
         CancellationToken token
         )
      {
         return Task.FromResult<ResponseCreator<HttpRequest, HttpContext>>( new LoginResponseCreator(
            this._config?.AuthenticationSchema,
            this._config?.LoginProvider,
            this._config?.UsernameFormName,
            this._config?.PasswordFormName,
            this._config?.UserIDTransformer
            ) );
      }
   }

   public class LoginConfiguration : AbstractPathBasedConfiguration
   {
      public const String DEFAULT_USERNAME_FORM_NAME = "username";
      public const String DEFAULT_PASSWORD_FORM_NAME = "password";

      public String AuthenticationSchema { get; set; }

      public String UsernameFormName { get; set; } = DEFAULT_USERNAME_FORM_NAME;

      public String PasswordFormName { get; set; } = DEFAULT_PASSWORD_FORM_NAME;

      [NestedDynamicConfiguration( "LoginProviderConfiguration" )]
      public LoginProvider LoginProvider { get; set; }

      [NestedDynamicConfiguration( "UserIDTransformerConfiguration" )]
      public StringTransformer UserIDTransformer { get; set; }
   }

   public interface LoginProvider
   {
      // Returns unique user id: e.g. a distinguished name (DN) in LDAP, or hash of the DN.
      ValueTask<String> PerformAuthenticationAsync( String username, String password );
   }

   public class LoginResponseCreator : ResponseCreator<HttpRequest, HttpContext>
   {
      private readonly String _authSchema;
      private readonly LoginProvider _authenticator;
      private readonly String _username;
      private readonly String _password;
      private readonly StringTransformer _userIDTransformer;
      private readonly DefaultLocklessInstancePoolForClasses<ResizableArray<Byte>> _bufferPool;
      private readonly StreamCharacterReaderLogic _reader;

      public LoginResponseCreator(
         String authSchema,
         LoginProvider authenticator,
         String username,
         String password,
         StringTransformer userIDTransformer
         )
      {
         this._authSchema = authSchema;
         this._authenticator = authenticator;
         this._username = username;
         this._password = password;
         this._userIDTransformer = userIDTransformer;
         this._bufferPool = new DefaultLocklessInstancePoolForClasses<ResizableArray<Byte>>();
         this._reader = new StreamCharacterReaderLogic( new UTF8Encoding( false, false ).CreateDefaultEncodingInfo() );
      }

      public async Task ProcessForResponseAsync(
         Object matchResult,
         HttpContext context,
         AuthenticatorAggregator<HttpRequest, HttpContext> authenticators
         )
      {
         var request = context.Request;
         var sentAnythingBack = false;
         if ( request.Method == HTTPConstants.METHOD_POST )
         {

            String username = null;
            String pw = null;
            Int64? len;
            if ( request.HasFormContentType )
            {
               if ( request.Form.TryGetValue( this._username, out var usernames )
                  && request.Form.TryGetValue( this._password, out var pws )
                  && usernames.Count == 1
                  && pws.Count == 1 )
               {
                  username = usernames[0];
                  pw = pws[0];
               }
            }
            else if ( String.Equals( request.ContentType, "application/json", StringComparison.OrdinalIgnoreCase )
               && ( len = request.ContentLength ).HasValue
               && len.Value < 1024 * 1024 * 1024 // 1MB

               )
            {
               var buffer = this._bufferPool.TakeInstance();
               var len32 = (Int32) len;
               try
               {
                  if ( buffer == null )
                  {
                     buffer = new ResizableArray<Byte>();
                  }
                  var stream = StreamFactory.CreateLimitedReader( request.Body, len32, buffer: buffer, token: context.RequestAborted );
                  var boundReader = ReaderFactory.NewNullableMemorizingValueReader(
                     this._reader,
                     stream
                     );
                  // Allow underlying stream buffer to become roughly max 1024 bytes length
                  JObject formData;
                  using ( boundReader.ClearStreamWhenStreamBufferTooBig( stream, 1024 ) )
                  {
                     formData = ( await UtilPack.JSON.JTokenStreamReader.Instance.TryReadNextAsync( boundReader ) ) as JObject;
                  }
                  username = formData.TryGetValue( this._username, out var jUsername ) ?
                     ( jUsername as JValue )?.Value as String :
                     null;
                  pw = formData.TryGetValue( this._password, out var jPassword ) ?
                     ( jPassword as JValue )?.Value as String :
                     null;
               }
               finally
               {
                  if ( buffer != null )
                  {
                     Array.Clear( buffer.Array, 0, buffer.Array.Length );
                     this._bufferPool.ReturnInstance( buffer );
                  }
               }
            }


            Authenticator<HttpContext> authChecker = null;
            sentAnythingBack = !String.IsNullOrEmpty( username )
               && !String.IsNullOrEmpty( pw )
               && ( authChecker = authenticators.GetAuthenticator( request, this._authSchema, isAuthenticationAttempt: true ) ) != null;
            if ( sentAnythingBack )
            {
               var challengeResult = await authChecker.ChallengeAsync( context );
               if ( !challengeResult.IsSuccess() )
               {
                  // We really need to do the login now
                  var userID = await this._authenticator.PerformAuthenticationAsync( username, pw );
                  if ( String.IsNullOrEmpty( userID ) )
                  {
                     // This will send 401
                     await challengeResult.Proceed();
                  }
                  else
                  {
                     if ( this._userIDTransformer != null )
                     {
                        userID = this._userIDTransformer.Transform( userID );
                     }
                     // This will send auth ID back to client.
                     await authChecker.RegisterUser( context, userID );
                  }
               }
               else
               {
                  // User already authenticated, just proceed (this will send auth ID back to client)
                  await challengeResult.Proceed();
               }
            }

         }

         if ( !sentAnythingBack )
         {
            // Malformed request
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync( "" );
         }
      }
   }

}
