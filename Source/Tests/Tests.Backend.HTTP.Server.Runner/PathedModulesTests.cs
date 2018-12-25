/*
 * Copyright 2018 Stanislav Muhametsin. All rights Reserved.
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
using Backend.HTTP.Server.Runner;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.Backend.HTTP.Server.Runner
{
   [TestClass]
   public class PathedModulesTests
   {
      [TestMethod, Timeout( 60000 )]
      public async Task PerformTest()
      {
         var configFile = Environment.GetEnvironmentVariable( "BACKEND_TEST_RUNNER_CONFIG" );
         // This structure is a bit awkward, until the Server.Runner is rewritten to be better.
         var serverTask = Program.Main( new[] { "--ConfigurationFile", configFile } );
         var testTask = RunTestsOnServer(
               new ConfigurationBuilder()
               .AddJsonFile( configFile )
               .Build()
               .Get<TestConfig>()
               );

         var completion = await Task.WhenAny( serverTask, testTask );
         Assert.IsTrue( ReferenceEquals( completion, testTask ) );
      }

      //private enum TaskCompletion
      //{
      //   Server,
      //   Test
      //}

      //private static async Task<T> AwaitAndThen<T>( Task t, T then )
      //{
      //   await t;
      //   return then;
      //}

      private static async Task RunTestsOnServer(
         TestConfig config
         )
      {
         var serverEP = ( await config.Connection.EndPoints.ToIPEndPoints() )[0].EndPoint;
         // Wait till endpoint becomes available
         var available = false;
         do
         {
            try
            {
               await Task.Delay( 100 );
               using ( var sock = new Socket( serverEP.AddressFamily, SocketType.Stream, ProtocolType.Tcp ) )
               {
                  await sock.ConnectAsync( serverEP );
               }
               available = true;
            }
            catch
            {

            }

         } while ( !available );

         var request = new HttpRequestMessage( HttpMethod.Get, "http://" + serverEP );
         //request.Headers.Add( "Accept", "application/vnd.github.v3+json" );
         //request.Headers.Add( "User-Agent", "HttpClientFactory-Sample" );

         using ( var httpClient = new HttpClient() )
         {
            var response = await httpClient.SendAsync( request );
            var responseString = await response.Content.ReadAsStringAsync();
            Assert.AreEqual( config.ResponseCreators[0].Configuration.WhatToSay, responseString );
         }

      }


      private sealed class TestConfig
      {
         public TestResponseCreatorConfig[] ResponseCreators { get; set; }
         public global::Backend.HTTP.Server.Initialization.ConnectionConfiguration Connection { get; set; }
      }

      private sealed class TestResponseCreatorConfig
      {
         public SampleBackendOperationConfiguration Configuration { get; set; }
      }
   }

   public sealed class SampleBackendOperationFactory : PathBasedRegexMatchingResponseCreatorFactory
   {
      private readonly SampleBackendOperationConfiguration _config;

      public SampleBackendOperationFactory(
         SampleBackendOperationConfiguration config
         ) : base( config )
      {
         this._config = config;
      }

      protected override Task<ResponseCreator<HttpRequest, HttpContext>> DoCreateResponseCreatorAsync(
         ResponseCreatorInstantiationParameters creationParameters,
         CancellationToken token
         )
      {
         return Task.FromResult<ResponseCreator<HttpRequest, HttpContext>>( new SampleResponseCreator( this._config.WhatToSay ) );
      }
   }

   public sealed class SampleBackendOperationConfiguration : AbstractPathBasedConfiguration
   {
      public String WhatToSay { get; set; }
   }

   public sealed class SampleResponseCreator : PublicResponseCreator
   {
      private readonly String _whatToSay;
      private readonly Encoding _encoding;

      public SampleResponseCreator( String whatToSay )
      {
         this._whatToSay = whatToSay;
         this._encoding = new UTF8Encoding( false, false );
      }

      protected override Task ProcessForResponseAsync( Object matchResult, HttpContext context )
      {
         return context.Response.WriteAsync(
            this._whatToSay,
            this._encoding,
            context.RequestAborted
            );
      }
   }

}
