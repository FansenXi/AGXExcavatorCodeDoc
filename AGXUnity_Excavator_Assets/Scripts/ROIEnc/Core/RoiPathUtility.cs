using System.IO;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Core
{
  public static class RoiPathUtility
  {
    private const string RepoRootFolderName = "AGXUnity_Excavator";

    public static string ResolveConfiguredPath( string configuredPath )
    {
      if ( string.IsNullOrWhiteSpace( configuredPath ) )
        return ResolveRepoRoot();

      if ( Path.IsPathRooted( configuredPath ) )
        return Path.GetFullPath( configuredPath );

      var normalizedPath = configuredPath.Replace( '\\', Path.DirectorySeparatorChar )
                                         .Replace( '/', Path.DirectorySeparatorChar );
      if ( normalizedPath.StartsWith( $"Assets{Path.DirectorySeparatorChar}" ) )
        return Path.GetFullPath( Path.Combine( ResolveProjectRoot(), normalizedPath ) );

      return Path.GetFullPath( Path.Combine( ResolveRepoRoot(), normalizedPath ) );
    }

    public static string ResolveOutputDirectory( string configuredPath )
    {
      return ResolveConfiguredPath( configuredPath );
    }

    public static string ResolveProjectRoot()
    {
      return Path.GetFullPath( Path.Combine( Application.dataPath, ".." ) );
    }

    public static string ResolveRepoRoot()
    {
      var repoRoot = Path.Combine( Application.dataPath, RepoRootFolderName );
      if ( Directory.Exists( repoRoot ) )
        return Path.GetFullPath( repoRoot );

      return ResolveProjectRoot();
    }
  }
}
