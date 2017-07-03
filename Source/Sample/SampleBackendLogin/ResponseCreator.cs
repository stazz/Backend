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
using Backend.HTTP.Common.Login;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;
using UtilPack.Configuration;

namespace SampleBackendLogin
{
   // Obviously, this is NOT how you should do login in your actual app.
   [ConfigurationType( typeof( SampleLoginConfiguration ) )]
   public class SampleLoginProvider : LoginProvider
   {
      private readonly String _username;
      private readonly String _password;

      public SampleLoginProvider( SampleLoginConfiguration config )
      {
         this._username = config.Username ?? SampleLoginConfiguration.DEFAULT_USERNAME;
         this._password = config.Password ?? SampleLoginConfiguration.DEFAULT_PASSWORD;
      }

      public ValueTask<String> PerformAuthenticationAsync( String username, String password )
      {
         return new ValueTask<String>( String.Equals( this._username, username ) && String.Equals( this._password, password ) ? username : null );
      }
   }

   public class SampleLoginConfiguration
   {
      public const String DEFAULT_USERNAME = "sample";
      public const String DEFAULT_PASSWORD = DEFAULT_USERNAME;
      public String Username { get; set; } = DEFAULT_USERNAME;
      public String Password { get; set; } = DEFAULT_USERNAME;
   }
}
