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
using Microsoft.Extensions.Configuration;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGetUtils.Lib.AssemblyResolving;
using NuGetUtils.Lib.Common;
using NuGetUtils.Lib.Restore;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UtilPack;
using UtilPack.Configuration;

namespace Backend.Core.Initialization
{

   public class DynamicElementManager<T> : IDisposable
   {
      private readonly BoundRestoreCommandUser _restorer;
      private readonly System.Runtime.Loader.AssemblyLoadContext _loader;

      public DynamicElementManager(
         BoundRestoreCommandUser restorer,
         NuGetAssemblyResolver resolver,
         System.Runtime.Loader.AssemblyLoadContext loader,
         Func<Task<T>> factory
         )
      {
         this._restorer = restorer;
         this.NuGetAssemblyResolver = resolver;
         this._loader = loader;
         this.Instance = new AsyncLazy<T>( factory );
      }

      public AsyncLazy<T> Instance { get; }

      public void Dispose()
      {
         this.NuGetAssemblyResolver.DisposeSafely();
         this._restorer.DisposeSafely();
      }

      public NuGetAssemblyResolver NuGetAssemblyResolver { get; }
   }

   public static class DynamicElementManagerFactory
   {
      public static DynamicElementManager<T> CreateFromConfig<T>(
         String configurationLocation,
         Boolean copyAssembliesBeforeLoading,
         NuGetFramework thisFramework,
         String defaultNuGetConfigFileLocation,
         SourceCacheContext sourceCacheContext,
         LocalPackageFileCache localPackageFileCache,
         LockFile runtimeFrameworkPackages,
         String runtimeID,
         IConfiguration configuration,
         Func<NuGetAssemblyResolver, Task<T>> creator,
         Func<AssemblyName, Boolean> loadAssemblyFromParentContext
         )
      {
         var thisTempFolder = copyAssembliesBeforeLoading ? new Lazy<String>( () =>
         {
            var tempFolder = Path.Combine( Path.GetTempPath(), "BackendComponent_" + Guid.NewGuid() );
            Directory.CreateDirectory( tempFolder );
            return tempFolder;
         }, LazyThreadSafetyMode.ExecutionAndPublication ) : null;

         var restorer = new BoundRestoreCommandUser(
            GetNuGetSettings( ProcessPathValue( configurationLocation, configuration.GetSection( "NuGetConfigurationFile" ).Get<String>() ), defaultNuGetConfigFileLocation ),
            thisFramework: thisFramework,
            runtimeIdentifier: runtimeID,
            nugetLogger: new TextWriterLogger( new TextWriterLoggerOptions()
            {
               DebugWriter = null
            } ),
            sourceCacheContext: sourceCacheContext,
            nuspecCache: localPackageFileCache
            );
         var resolver = NuGetAssemblyResolverFactory.NewNuGetAssemblyResolver(
            restorer,
            out var loader,
            thisFrameworkRestoreResult: runtimeFrameworkPackages,
            pathProcessor: thisTempFolder == null ? null as Func<String, String> : ( originalPath =>
            {
               var actualPath = Path.Combine( thisTempFolder.Value, Path.GetFileName( originalPath ) );
               File.Copy( originalPath, actualPath, false );
               return actualPath;
            } ),
            additionalCheckForDefaultLoader: assemblyName =>
            {
               var simpleName = assemblyName.Name;
               switch ( simpleName )
               {
                  case "Backend.Core":
                  case "UtilPack.Configuration": // In order to make sure stuff like [ConfigurationType] work.
                  case "System.ValueTuple":
                     return true;
                  default:
                     return ( loadAssemblyFromParentContext?.Invoke( assemblyName ) ?? false )
                     || simpleName.StartsWith( "Microsoft.Extensions." );
               }
            }
            );
         return new DynamicElementManager<T>(
            restorer,
            resolver,
            loader,
            () => creator( resolver )
            );
      }

      public static ISettings GetNuGetSettings(
         String thisConfigFileLocation,
         String defaultConfigFileLocation
         )
      {
         ISettings nugetSettings;
         if ( String.IsNullOrEmpty( thisConfigFileLocation ) && String.IsNullOrEmpty( thisConfigFileLocation = defaultConfigFileLocation ) )
         {
            nugetSettings = Settings.LoadDefaultSettings( Path.GetDirectoryName( new Uri( typeof( DynamicElementManagerFactory ).GetTypeInfo().Assembly.CodeBase ).LocalPath ), null, new XPlatMachineWideSetting() );
         }
         else
         {
            var fp = Path.GetFullPath( thisConfigFileLocation );
            nugetSettings = Settings.LoadSpecificSettings( Path.GetDirectoryName( fp ), Path.GetFileName( fp ) );
         }

         return nugetSettings;
      }


      public static String ProcessPathValue( String configurationLocation, String path )
      {
         if ( !String.IsNullOrEmpty( path ) && !Path.IsPathRooted( path ) )
         {
            path = Path.GetFullPath( Path.Combine( Path.GetDirectoryName( configurationLocation ), path ) );
         }

         return path;
      }
   }

   public static class BackendInitializationExtensions
   {
      internal const String PACKAGE_ID = "PackageID";
      internal const String PACKAGE_VERSION = "PackageVersion";
      internal const String ASSEMBLY_PATH = "AssemblyPath";
      internal const String TYPE_NAME = "TypeName";

      public static async Task<T> InstantiateFromConfiguration<T>(
         this NuGetAssemblyResolver resolver,
         IConfiguration configuration,
         String configurationPath,
         CancellationToken token,
         String typeConfigName = "Type"
         )
         where T : class
      {

         // 1. Collect all nuget package refs
         var typeConfigSetion = configuration.GetSection( typeConfigName );
         var allNuGetPackages = typeConfigSetion.GetAllNuGetPackages().ToImmutableArray();
         var allPathBasedAssemblies = typeConfigSetion.GetAllPathBasedAssemblies( configurationPath ).ToImmutableArray();
         // Perform one big restore for all used NuGet packages
         var nugetAssemblies = allNuGetPackages.Length > 0 ? ( await resolver.LoadNuGetAssemblies( token, allNuGetPackages.Select( t => (t.PackageID, t.PackageVersion, t.AssemblyPath) ).ToArray() ) )
            .Select( ( ass, idx ) => (allNuGetPackages[idx].PackageID, allNuGetPackages[idx].AssemblyPath, ass) )
            .Distinct( ComparerFromFunctions.NewEqualityComparer<(String PackageID, String AssemblyPath, Assembly Assembly)>( ( x, y ) => (x.PackageID, x.AssemblyPath).Equals( (y.PackageID, y.AssemblyPath) ), x => x.PackageID.GetHashCode() ) )
            .ToImmutableDictionary( tuple => (tuple.PackageID, tuple.AssemblyPath), tuple => tuple.Item3 ) :
            ImmutableDictionary<(String, String), Assembly>.Empty;
         var pathLoader = allPathBasedAssemblies.Length > 0 ? new PathBasedAssemblyLoader( allPathBasedAssemblies.Select( p => p.AssemblyPath ) ) : null;

         var loader = new DynamicConfigurableTypeLoader( ( typeConfig, targetType ) =>
         {
            typeConfig = typeConfig.GetSection( typeConfigName );
            var packageID = GetRequiredStringValue( typeConfig, PACKAGE_ID );

            Assembly typeAssembly = null;
            if ( !String.IsNullOrEmpty( packageID ) )
            {
               // This is NuGet package type configuration
               nugetAssemblies.TryGetValue( (packageID, GetOptionalStringValue( typeConfig, PACKAGE_VERSION )), out typeAssembly );
            }
            else
            {
               var assemblyPath = GetRequiredStringValue( typeConfig, ASSEMBLY_PATH );
               if ( !String.IsNullOrEmpty( assemblyPath ) )
               {
                  // This is path-based type configuration
                  typeAssembly = pathLoader.LoadFromAssemblyName( System.Runtime.Loader.AssemblyLoadContext.GetAssemblyName( DynamicElementManagerFactory.ProcessPathValue( configurationPath, assemblyPath ) ) );
               }
            }

            TypeInfo loadedType = null;
            if ( typeAssembly != null )
            {
               // Search for type within the assembly
               var typeName = GetOptionalStringValue( typeConfig, TYPE_NAME );


               var checkParentType = !String.IsNullOrEmpty( typeName );

               if ( checkParentType )
               {
                  // Instantiate directly
                  loadedType = typeAssembly.DefinedTypes.FirstOrDefault( t => String.Equals( t.FullName, typeName ) );
               }
               else
               {
                  // Search for first available
                  loadedType = typeAssembly.DefinedTypes.FirstOrDefault( t => !t.IsAbstract && targetType.IsAssignableFrom( t ) );
               }

               if ( loadedType?.IsGenericTypeDefinition ?? false )
               {
                  throw new NotImplementedException( "TODO: implement generic parameter substitution to type loader." );
               }
            }

            return new ValueTask<TypeInfo>( loadedType );
         } );

         return (T) ( await loader.InstantiateWithConfiguration( configuration, typeof( T ).GetTypeInfo(), typeInfo =>
         {
            return DynamicConfigurableTypeLoader.DefaultConfigurationTypeLoaderCallback( typeInfo ) ??
               typeInfo // If no [ConfigurationType] is provided, then try to bind to public constructor's single parameter type, if suitable constructor is found
                  .DeclaredConstructors
                  .Where( ctor => ctor.IsPublic && ctor.GetParameters().Length == 1 )
                  .FirstOrDefault()
                  ?.GetParameters()[0].ParameterType;
         } ) );
      }

      public static IEnumerable<(String PackageID, String PackageVersion, String AssemblyPath, String TypeName)> GetAllNuGetPackages(
         this IConfiguration configuration
         )
      {
         return configuration
            .AsDepthFirstEnumerable( c => c.GetChildren() )
            .Select( c => c.TryGetNuGetPackageConfiguration() )
            .Where( t => t.HasValue )
            .Select( t => t.Value );
      }

      public static (String PackageID, String PackageVersion, String AssemblyPath, String TypeName)? TryGetNuGetPackageConfiguration(
         this IConfiguration configuration
         )
      {
         // Basically, we need to scan for all sections containing:
         // "PackageID": mandatory, string
         // "PackageVersion": optional, string
         // "AssemblyPath": optional, string
         // "TypeName": optional, string
         String pID;
         if (
            configuration.GetChildren().Any()
            && !String.IsNullOrEmpty( pID = GetRequiredStringValue( configuration, PACKAGE_ID ) )
            )
         {
            return (pID, GetOptionalStringValue( configuration, PACKAGE_VERSION ), GetOptionalStringValue( configuration, ASSEMBLY_PATH ), GetOptionalStringValue( configuration, TYPE_NAME ));
         }
         else
         {
            return null;
         }
      }

      public static IEnumerable<(String AssemblyPath, String TypeName)> GetAllPathBasedAssemblies(
         this IConfiguration configuration,
         String configurationPath
         )
      {
         return configuration
            .AsDepthFirstEnumerable( c => c.GetChildren() )
            .Select( c => c.TryGetPathConfiguration( configurationPath ) )
            .Where( t => t.HasValue )
            .Select( t => t.Value );
      }

      public static (String AssemblyPath, String TypeName)? TryGetPathConfiguration(
         this IConfiguration configuration,
         String configurationPath
         )
      {
         // "AssemblyPath": mandatory, string
         // "TypeName": optional, string
         String path;
         if (
            configuration.GetChildren().Any()
            && !String.IsNullOrEmpty( path = GetRequiredStringValue( configuration, ASSEMBLY_PATH ) )
            )
         {
            return (DynamicElementManagerFactory.ProcessPathValue( configurationPath, path ), GetOptionalStringValue( configuration, TYPE_NAME ));
         }
         else
         {
            return default;
         }
      }

      internal static String GetRequiredStringValue( IConfiguration config, String name )
      {
         var subSection = config.GetSection( name );
         return subSection.GetChildren().Any() ? // No children - this is terminating node
            null :
            subSection.Value; // We have some actual value
      }

      internal static String GetOptionalStringValue( IConfiguration config, String name )
      {
         var subSection = config.GetSection( name );
         return subSection.GetChildren().Any() ? // No children - this is terminating node
            null :
            subSection.Value;
      }

      private sealed class PathBasedAssemblyLoader : System.Runtime.Loader.AssemblyLoadContext
      {

         private readonly ImmutableDictionary<String, Lazy<Assembly>> _assemblies;
         private readonly System.Runtime.Loader.AssemblyLoadContext _parentLoadContext;


         public PathBasedAssemblyLoader( IEnumerable<String> assemblyPaths )
         {
            this._parentLoadContext = GetLoadContext( this.GetType().GetTypeInfo().Assembly );
            this._assemblies = assemblyPaths
               .Select( ap => Path.GetFullPath( ap ) )
               .Select( ap => Path.GetDirectoryName( ap ) )
               .Distinct()
               .SelectMany( directory => Directory.EnumerateFiles( directory, "*.dll", SearchOption.TopDirectoryOnly ) )
               .ToImmutableDictionary( path => Path.GetFileNameWithoutExtension( path ), path => new Lazy<Assembly>( () => this.LoadFromAssemblyPath( path ), LazyThreadSafetyMode.ExecutionAndPublication ) );
         }


         protected override Assembly Load( AssemblyName assemblyName )
         {
            // Try parent first, in case we have common assemblies in the target folder
            Assembly retVal = null;
            try
            {
               retVal = this._parentLoadContext.LoadFromAssemblyName( assemblyName );
            }
            catch
            {

            }
            return retVal ?? this._assemblies.GetOrDefault( assemblyName.Name )?.Value ?? throw new InvalidOperationException( "Could not load assembly " + assemblyName );
         }

         protected override IntPtr LoadUnmanagedDll( String unmanagedDllName )
         {
            throw new NotImplementedException();
            //return this.LoadUnmanagedDllFromPath( Path.Combine( this._directory, unmanagedDllName ) );
         }
      }
   }
}
