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
using System.Text.RegularExpressions;

namespace Backend.HTTP.Common
{
   public abstract class PathBasedRegexMatchingResponseCreatorFactory : RegexBasedResponseCreatorFactory<HttpRequest, HttpRequest, HttpContext, ResponseCreatorInstantiationParameters>
   {
      public PathBasedRegexMatchingResponseCreatorFactory(
         String pathMatch
         ) : base( req => req.Path, new Regex( "^/" + pathMatch + "$", RegexOptions.Singleline | RegexOptions.IgnoreCase ) )
      {

      }

      public PathBasedRegexMatchingResponseCreatorFactory(
         AbstractPathBasedConfiguration config
         ) : this( config?.MatchPath )
      {

      }
   }

   public class AbstractPathBasedConfiguration
   {
      public String MatchPath { get; set; }
   }

   public abstract class AuthenticatorFactoryImpl : AuthenticatorFactoryImpl<HttpContext, HttpRequest, AuthenticationDataHolder>
   {

   }
}
