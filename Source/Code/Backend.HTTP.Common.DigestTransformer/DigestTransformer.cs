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
using FluentCryptography.Digest;
using System;
using UtilPack;

namespace Backend.HTTP.Common.DigestTransformer
{
   public class DigestTransformerConfiguration
   {
      // TODO configuration which specifies string (SHA128, SHA256, etc...) for algorithm
      public Int32 Base64EncodeShuffleSeed { get; set; } = new Random().Next();
   }

   public class DigestTransformer : StringTransformer
   {

      private readonly Char[] _base64EncodeChars;
      private readonly BlockDigestAlgorithm _algorithm;

      public DigestTransformer( DigestTransformerConfiguration configuration )
      {
         if ( configuration == null )
         {
            configuration = new DigestTransformerConfiguration();
         }

         this._base64EncodeChars = DigestBasedRandomGenerator.ShuffleBase64CharactersFromSeed( configuration.Base64EncodeShuffleSeed, isURLSafe: true );
         this._algorithm = new SHA512();
      }

      public String Transform( String input )
      {
         return StringConversions.EncodeBinary( this._algorithm.ComputeDigest( System.Text.Encoding.UTF8.GetBytes( input ) ), this._base64EncodeChars );
      }
   }
}
