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
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Backend.HTTP.Common
{
   public abstract class AuthenticationGuardedResponseCreator : AuthenticationGuardedResponseCreator<HttpRequest, HttpContext>
   {
      protected const String METHOD_GET = HTTPConstants.METHOD_GET;
      protected const String METHOD_POST = HTTPConstants.METHOD_POST;

      public AuthenticationGuardedResponseCreator( String authenticationSchema )
         : base( authenticationSchema )
      {
      }

      protected override HttpRequest GetAuthenticatorMatchContext( HttpContext context )
      {
         return context.Request;
      }
   }

   public abstract class PublicResponseCreator : PublicResponseCreator<HttpRequest, HttpContext>
   {
      protected const String METHOD_GET = HTTPConstants.METHOD_GET;
      protected const String METHOD_POST = HTTPConstants.METHOD_POST;
   }

   public static class HTTPConstants
   {

      public const String METHOD_GET = "GET";
      public const String METHOD_POST = "POST";
   }


}
