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
using Backend.HTTP.Common;
using System;
using UtilPack.Configuration;
using Backend.Core;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SampleBackendOperation
{
   /// <summary>
   /// This class will be called by Backend.HTTP.Server when initializing.
   /// It should be public, and have public constructor.
   /// </summary>
   /// <remarks>
   /// The <see cref="ConfigurationTypeAttribute"/> attribute is present in order to provide access to configuration file, the constructor should take one argument of that type.
   /// The server will take care of instantiating and populating configuration object.
   /// </remarks>
   [ConfigurationType( typeof( SampleOperationConfiguration ) )]
   public class SampleBackendOperationFactory : PathBasedRegexMatchingResponseCreatorFactory
   {
      private readonly SampleOperationConfiguration _config;

      public SampleBackendOperationFactory( SampleOperationConfiguration config )
         : base( config )
      {
         this._config = config;
      }

      protected override ValueTask<ResponseCreator<HttpRequest, HttpContext>> DoCreateResponseCreatorAsync(
         ResponseCreatorInstantiationParameters creationParameters,
         CancellationToken token
         )
      {
         return new ValueTask<ResponseCreator<HttpRequest, HttpContext>>( new SampleOperation( this._config.WhatToSay ) );
      }
   }

   public class SampleOperationConfiguration : AbstractPathBasedConfiguration
   {
      public String WhatToSay { get; set; }
   }
}
