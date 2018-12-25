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
using Backend.HTTP.Common.Login;
using Novell.Directory.Ldap;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using UtilPack;

namespace Backend.HTTP.Server.Login.LDAP
{
   public class LDAPLoginProvider : LoginProvider
   {
      private readonly LDAPServerLoginProvider[] _serverAuthenticators;

      public LDAPLoginProvider( LDAPAuthenticatorConfiguration configuration )
      {
         ArgumentValidator.ValidateNotNull( nameof( configuration ), configuration );
         var distinguishedNames = new ConcurrentDictionary<UserDNKey, String>();

         LDAPAuthenticatorConnectionConfiguration prevConfig = null;
         this._serverAuthenticators = ( configuration?.LDAPServers ?? Empty<LDAPAuthenticatorConnectionConfiguration>.Array )
            .Select( connConfig => new LDAPServerLoginProvider( new LDAPOptions(
                connConfig.Host ?? prevConfig?.Host,
                connConfig.Port ?? prevConfig?.Port,
                connConfig.QueryUser ?? prevConfig?.QueryUser,
                connConfig.QueryPassword ?? prevConfig?.QueryPassword,
                connConfig.QueryRoot ?? prevConfig?.QueryRoot,
                connConfig.QueryFilter ?? prevConfig?.QueryFilter,
                connConfig.DefaultDomain ?? prevConfig?.DefaultDomain
                ),
                distinguishedNames
                ) )
            .ToArray();
      }

      public async ValueTask<String> PerformAuthenticationAsync( String username, String password )
      {
         String dn = null;
         for ( var i = 0; i < this._serverAuthenticators.Length && dn == null; ++i )
         {
            dn = await this._serverAuthenticators[i].PerformAuthenticationAsync( username, password );
         }

         return dn;
      }


   }

   public class LDAPAuthenticatorConfiguration
   {
      public LDAPAuthenticatorConnectionConfiguration[] LDAPServers { get; set; }
   }

   public class LDAPAuthenticatorConnectionConfiguration
   {
      public String Host { get; set; }

      public Int32? Port { get; set; } = LdapConnection.DEFAULT_PORT;

      public String QueryUser { get; set; }

      public String QueryPassword { get; set; }

      public String QueryRoot { get; set; }

      public String QueryFilter { get; set; }

      public String DefaultDomain { get; set; }

   }

   internal sealed class LDAPOptions
   {
      public LDAPOptions(
         String host,
         Int32? port,
         String queryUser,
         String queryPassword,
         String queryRoot,
         String queryFilter, // {0} -> provided user name, {1} -> given domain
         String defaultDomain
         )
      {
         ArgumentValidator.ValidateNotNull( "Host", host );
         ArgumentValidator.ValidateNotNull( "Query user", queryUser );
         ArgumentValidator.ValidateNotNull( "Query password", queryPassword );
         ArgumentValidator.ValidateNotNull( "Query root", queryRoot );

         this.Host = host;
         this.Port = port ?? LdapConnection.DEFAULT_PORT;
         this.QueryUser = queryUser;
         this.QueryPassword = queryPassword;
         this.QueryRoot = queryRoot;
         this.QueryFilter = queryFilter;
         this.DefaultDomain = defaultDomain;
      }

      public String Host { get; }

      public Int32 Port { get; }

      public String QueryUser { get; }

      public String QueryPassword { get; }

      public String QueryRoot { get; }

      public String QueryFilter { get; }

      public String DefaultDomain { get; }
   }



   internal sealed class LDAPServerLoginProvider
   {


      private readonly LDAPOptions _ldapOptions;
      private readonly ConcurrentDictionary<UserDNKey, String> _distinguishedNames;

      internal LDAPServerLoginProvider(
         LDAPOptions ldapOptions,
         ConcurrentDictionary<UserDNKey, String> distinguishedNamesCache
         )
      {
         this._ldapOptions = ArgumentValidator.ValidateNotNull( nameof( ldapOptions ), ldapOptions );
         this._distinguishedNames = ArgumentValidator.ValidateNotNull( nameof( distinguishedNamesCache ), distinguishedNamesCache );
      }

      public Task<String> PerformAuthenticationAsync( String username, String password )
      {
         var ldapOptions = this._ldapOptions;
         String domainName;
         Int32 idx;
         if ( ( idx = username.IndexOf( '\\' ) ) != -1 )
         {
            domainName = username.Substring( 0, idx );
            username = username.Substring( idx + 1 );
         }
         else
         {
            domainName = ldapOptions.DefaultDomain;
         }

         return Task.Run( () =>
         {
            String retVal = null;
            var key = new UserDNKey( username, domainName );
            try
            {
               String dn;
               if ( !this._distinguishedNames.TryGetValue( key, out dn ) )
               {
                  dn = this.GetDistinguishedName( username, domainName );
                  if ( !dn.IsNullOrEmpty() )
                  {
                     this._distinguishedNames[key] = dn;
                  }
               }

               if ( !dn.IsNullOrEmpty() )
               {
                  using ( var conn = new LdapConnection() )
                  {
                     conn.Connect( ldapOptions.Host, ldapOptions.Port );
                     conn.Bind( LdapConnection.Ldap_V3, dn, password ); // This will throw if password invalid
                     retVal = dn;
                  }
               }
            }
            catch
            {
               // Ignore...
            }

            return retVal;
         } );
      }

      private String GetDistinguishedName( String providedUsername, String domainName )
      {
         var ldapOptions = this._ldapOptions;
         String retVal = null;
         using ( var conn = new LdapConnection() )
         {
            conn.Connect( ldapOptions.Host, ldapOptions.Port );
            conn.Bind( LdapConnection.Ldap_V3, ldapOptions.QueryUser, ldapOptions.QueryPassword );

            var qFilter = "(sAMAccountName={0})";
            var additionalFilter = ldapOptions.QueryFilter;
            if ( !additionalFilter.IsNullOrEmpty() )
            {
               qFilter = "(&" + qFilter + additionalFilter + ")";
            }

            var searchResults = conn.Search(
               ldapOptions.QueryRoot,
               LdapConnection.SCOPE_SUB,
               String.Format( qFilter, providedUsername, domainName ),
               null,
               false
               );

            if ( searchResults.hasMore() )
            {
               // Actually we get 4 entries, but the rest are referrals...
               var nextEntry = searchResults.next();

               retVal = nextEntry.DN;
            }
         }

         return retVal;
      }
   }

   internal struct UserDNKey : IEquatable<UserDNKey>
   {
      public UserDNKey( String username, String domainname )
      {
         this.Username = username;
         this.Domainname = domainname;
      }

      public String Username { get; }

      public String Domainname { get; }

      public override Boolean Equals( Object obj )
      {
         return obj is UserDNKey && this.Equals( (UserDNKey) obj );
      }

      public override Int32 GetHashCode()
      {

         return unchecked(( 17 * 21 + ( this.Username?.GetHashCode() ?? 0 ) ) * ( this.Domainname?.GetHashCode() ?? 0 ));
      }

      public Boolean Equals( UserDNKey other )
      {
         return String.Equals( this.Username, other.Username, StringComparison.OrdinalIgnoreCase )
            && String.Equals( this.Domainname, other.Domainname, StringComparison.OrdinalIgnoreCase );
      }
   }
}
