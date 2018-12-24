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
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Backend.Core;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Threading;

namespace Backend.HTTP.Common
{
   public class HTTPAuthenticatorAggregator : DefaultAuthenticatorAggregator<HttpRequest, HttpContext>
   {
      public HTTPAuthenticatorAggregator(
         IDictionary<String, Authenticator<HttpContext, HttpRequest>[]> checkers
         ) : base( checkers )
      {

      }

      public override Task ProceedWhenNoAuthenticatorFound( HttpContext context )
      {
         return PerformProceedWhenNoAuthenticatorFound( context.Response );
      }

      public static async Task PerformProceedWhenNoAuthenticatorFound( HttpResponse response )
      {
         response.StatusCode = 401;
         await response.WriteAsync( "" );
      }
   }

   public interface AuthenticationDataHolder : IDisposable
   {
      void AddAuthData( String authID, String userID, TimeSpan authIDExpirationTime );
      Boolean TryGetAuthData( String authID, out AuthenticatedTokenInfo authIDInfo, out AuthenticatedUserInfo authUserInfo );
      void RemoveAuthData( String authID );
   }

   public class AuthenticatedTokenInfo
   {
      private Object _lastAccessed;

      public AuthenticatedTokenInfo(
         String userID,
         TimeSpan expirationSpan
         )
      {
         this.UserID = userID ?? throw new ArgumentNullException( nameof( userID ) );
         this.ExpirationSpan = expirationSpan;
         this.MarkAccessedNow();
         this.UserID = userID;
      }

      public DateTime LastAccessed
      {
         get
         {
            return (DateTime) this._lastAccessed;
         }
      }

      public String UserID { get; }

      public TimeSpan ExpirationSpan { get; }

      public void MarkAccessedNow()
      {
         Interlocked.Exchange( ref this._lastAccessed, DateTime.UtcNow );
      }
   }

   public class AuthenticatedUserInfo
   {
      public AuthenticatedUserInfo(
         UserInfo userInfo
         )
      {
         this.UserInfo = userInfo ?? throw new ArgumentNullException( nameof( userInfo ) );
         this.AuthTokens = new HashSet<String>();
      }

      public UserInfo UserInfo { get; }

      public ISet<String> AuthTokens { get; }
   }
}
