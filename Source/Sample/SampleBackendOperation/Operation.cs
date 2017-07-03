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
using Microsoft.AspNetCore.Http;
using Backend.HTTP.Common;

namespace SampleBackendOperation
{
   internal sealed class SampleOperation : PublicResponseCreator
   {
      private readonly String _whatToSay;

      public SampleOperation( String whatToSay )
      {
         this._whatToSay = whatToSay;
      }

      protected override async Task ProcessForResponseAsync( object matchResult, HttpContext context )
      {
         await context.Response.WriteAsync( "This operation says: " + this._whatToSay );
      }
   }
}
