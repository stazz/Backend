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
using UtilPack;
using UtilPack.Configuration;
using UtilPack.Cryptography;
using UtilPack.Cryptography.Digest;

namespace Backend.HTTP.Common.DigestTransformer
{
   public class DigestTransformerConfiguration
   {
      // TODO configuration which specifies string (SHA128, SHA256, etc...) for algorithm
      public Int32 Base64EncodeShuffleSeed { get; set; } = new Random().Next();
   }

   [ConfigurationType( typeof( DigestTransformerConfiguration ) )]
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

         var chars = StringConversions.CreateBase64EncodeLookupTable( true );
         using ( var rng = new DigestBasedRandomGenerator( new SHA512(), 10, false ) )
         {
            rng.AddSeedMaterial( configuration.Base64EncodeShuffleSeed );
            using ( var secRandom = new SecureRandom( rng ) )
            {
               chars.Shuffle( secRandom );
            }
         }
         this._base64EncodeChars = chars;
         this._algorithm = new SHA256();
      }

      public String Transform( String input )
      {
         return StringConversions.EncodeBinary( this._algorithm.ComputeDigest( System.Text.Encoding.UTF8.GetBytes( input ) ), this._base64EncodeChars );
      }
   }
}
