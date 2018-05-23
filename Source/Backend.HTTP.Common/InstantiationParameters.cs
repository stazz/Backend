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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System;
using System.IO;

using TLogger = UtilPack.Logging.Publish.LogPublisher<Backend.HTTP.Common.HttpLogInfo>;

namespace Backend.HTTP.Common
{
   public class ResponseCreatorInstantiationParameters
   {
      public ResponseCreatorInstantiationParameters(
         String configurationFileLocation,
         IWebHostBuilder webHostBuilder,
         TLogger logger
         )
      {
         this.ConfigurationFileLocation = configurationFileLocation;
         this.WebHostBuilder = webHostBuilder ?? throw new ArgumentNullException( nameof( webHostBuilder ) );
         this.Logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
      }

      public String ConfigurationFileLocation { get; }

      public IWebHostBuilder WebHostBuilder { get; }

      public event Action<IApplicationBuilder> OnConfigureEvent;

      public Action<IApplicationBuilder> OnConfigureEventValue => this.OnConfigureEvent;

      public TLogger Logger { get; }
   }
}

public static partial class E_Backend
{
   public static String ProcessPathValue( this ResponseCreatorInstantiationParameters paramz, String path )
   {
      if ( !String.IsNullOrEmpty( path ) && !Path.IsPathRooted( path ) )
      {
         path = Path.GetFullPath( Path.Combine( Path.GetDirectoryName( paramz.ConfigurationFileLocation ), path ) );
      }

      return path;
   }
}