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
using Backend.HTTP.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Builder.Internal;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UtilPack;

namespace Backend.HTTP.Server.StaticFiles
{
   public class StaticFileFunctionalityFactory : PathBasedRegexMatchingResponseCreatorFactory
   {
      private readonly StaticFileConfiguration _config;

      public StaticFileFunctionalityFactory(
         StaticFileConfiguration configuration
         ) : base( configuration )
      {
         this._config = ArgumentValidator.ValidateNotNull( nameof( configuration ), configuration );
      }

      protected override Task<ResponseCreator<HttpRequest, HttpContext>> DoCreateResponseCreatorAsync(
         ResponseCreatorInstantiationParameters creationParameters,
         CancellationToken token
         )
      {
         var options = new StaticFileOptions( creationParameters, this._config );
         // We can't use UseFileServer extension method as it will use default middleware logic, and Backend.HTTP.Server will overwrite it with its .Use( ... ) call on application builder.
         // But at least we can get the mime type info from FileExtensionContentTypeProvider
         return Task.FromResult( this._config.RequiredAuthenticationSchema == null ?
            (ResponseCreator<HttpRequest, HttpContext>) new PublicStaticFileResponseCreator( options ) :
            new AuthenticationGuardedStaticFileResponseCreator( options ) );
      }
   }

   internal sealed class AuthenticationGuardedStaticFileResponseCreator : AuthenticationGuardedResponseCreator
   {
      private readonly StaticFileOptions _options;

      public AuthenticationGuardedStaticFileResponseCreator(
         StaticFileOptions options
         ) : base( options.AuthenticationSchema )
      {
         this._options = options;
      }

      protected override Task ProcessForResponseAsync( Object matchResult, HttpContext context, UserInfo userInfo )
      {
         return StaticFileResponseFunctionality.ProcessForResponse(
            (Match) matchResult,
            context,
            this._options
            );
      }
   }

   internal sealed class PublicStaticFileResponseCreator : PublicResponseCreator
   {
      private readonly StaticFileOptions _options;

      public PublicStaticFileResponseCreator(
         StaticFileOptions options
         )
      {
         this._options = options;
      }

      protected override Task ProcessForResponseAsync( Object matchResult, HttpContext context )
      {
         return StaticFileResponseFunctionality.ProcessForResponse(
            (Match) matchResult,
            context,
            this._options
            );
      }
   }

   internal sealed class StaticFileOptions
   {
      public StaticFileOptions(
         ResponseCreatorInstantiationParameters creationParams,
         StaticFileConfiguration configuration
         )
      {
         this.AuthenticationSchema = configuration.RequiredAuthenticationSchema;
         this.RootFolder = creationParams.ProcessPathValue( configuration.RootFolder );
         this.DefaultFiles = configuration.DefaultFiles ?? new[] { "index.html" };
         var contentTypeProvider = new FileExtensionContentTypeProvider();
         foreach ( var str in configuration.RemoveContentTypeMappings ?? Empty<String>.Array )
         {
            contentTypeProvider.Mappings.Remove( str );
         }
         //foreach ( var tuple in this.AddContentTypeMappings ?? Empty<String>.Array )
         //{
         //contentTypeProvider.Mappings.Add( tuple.Item1, tuple.Item2 );
         //}
         this.ContentTypeProvider = contentTypeProvider;
         this.DefaultContentType = configuration.DefaultContentType;

      }

      public String AuthenticationSchema { get; }
      public String RootFolder { get; }
      public String[] DefaultFiles { get; }
      public FileExtensionContentTypeProvider ContentTypeProvider { get; }
      public String DefaultContentType { get; }
   }

   internal static class StaticFileResponseFunctionality
   {
      public static async Task ProcessForResponse(
         Match matchResult,
         HttpContext context,
         StaticFileOptions options
         )
      {
         var originalPath = matchResult.Groups[1].Value;
         if ( originalPath.Length > 0 && ( originalPath[0] == Path.DirectorySeparatorChar || originalPath[0] == Path.AltDirectorySeparatorChar ) )
         {
            originalPath = "." + originalPath;
         }

         var path = Path.GetFullPath( Path.Combine( options.RootFolder, originalPath ) );
         var sendFile = true;
         var isDir = Directory.Exists( path );
         if ( isDir )
         {
            if ( !originalPath.EndsWith( "/" ) )
            {
               context.Response.StatusCode = 303;
               context.Response.Headers["location"] = context.Request.Path + "/"; // + originalPath + "/";
               sendFile = false;
            }


         }

         // TODO Caches, performance, etc (If-Modified-Since header support)
         if ( sendFile )
         {
            if (
               path.StartsWith( options.RootFolder )
               )
            {

               var i = 0;
               var max = options.DefaultFiles.Length;
               if ( isDir )
               {
                  while ( i < max && !File.Exists( path = Path.Combine( path, options.DefaultFiles[i] ) ) )
                  {
                     ++i;
                  }
               }

               if ( ( ( isDir && i < max ) || File.Exists( path ) )
               && (
                  options.ContentTypeProvider.TryGetContentType( path, out var contentType )
                  || !String.IsNullOrEmpty( contentType = options.DefaultContentType )
               ) )
               {
                  context.Response.ContentType = contentType;
                  await context.Response.SendFileAsync( path );
               }
               else
               {
                  context.Response.StatusCode = 404;
               }
            }
            else
            {
               context.Response.StatusCode = 404;
            }
         }
      }
   }

   public class StaticFileConfiguration : AbstractPathBasedConfiguration
   {
      public String RequiredAuthenticationSchema { get; set; }
      public String RootFolder { get; set; }
      public String[] DefaultFiles { get; set; }
      public String DefaultContentType { get; set; }
      public String[] RemoveContentTypeMappings { get; set; }
      //         (String, String)[] addMappings
   }
}
