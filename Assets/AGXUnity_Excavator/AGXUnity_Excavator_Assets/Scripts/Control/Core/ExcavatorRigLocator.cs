using AGXUnity_Excavator.Scripts;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Core
{
  public static class ExcavatorRigLocator
  {
    private static readonly string[] DefaultBucketSemanticNames =
    {
      "Bucket",
      "Shovel",
      "watou"
    };

    public static T ResolveComponent<T>( Component context, T current ) where T : Component
    {
      if ( ShouldKeepCurrent( current ) )
        return current;

      if ( context == null )
        return FindBestInScene( null, current );

      var localComponent = context.GetComponent<T>();
      if ( IsSelectable( localComponent ) )
        return localComponent;

      var parentComponent = context.GetComponentInParent<T>( true );
      if ( IsSelectable( parentComponent ) )
        return parentComponent;

      var childComponent = context.GetComponentInChildren<T>( true );
      if ( IsSelectable( childComponent ) )
        return childComponent;

      return FindBestInScene( context.transform, current );
    }

    public static T ResolveActiveComponent<T>( Component context, T current ) where T : Component
    {
      if ( IsSelectable( current ) )
        return current;

      if ( context == null )
        return FindBestActiveInScene<T>( null );

      var localComponent = context.GetComponent<T>();
      if ( IsSelectable( localComponent ) )
        return localComponent;

      var parentComponent = context.GetComponentInParent<T>( true );
      if ( IsSelectable( parentComponent ) )
        return parentComponent;

      var childComponent = context.GetComponentInChildren<T>( true );
      if ( IsSelectable( childComponent ) )
        return childComponent;

      return FindBestActiveInScene<T>( context.transform );
    }

    public static T ResolveActiveComponentInRoot<T>( Transform root, T current ) where T : Component
    {
      if ( root == null )
        return IsSelectable( current ) ? current : null;

      if ( IsSelectableInRoot( current, root ) )
        return current;

      var candidates = root.GetComponentsInChildren<T>( true );
      if ( candidates == null || candidates.Length == 0 )
        return null;

      foreach ( var candidate in candidates ) {
        if ( IsSelectableInRoot( candidate, root ) )
          return candidate;
      }

      return null;
    }

    public static T ResolveComponentInRoot<T>( Transform root, T current ) where T : Component
    {
      if ( root == null )
        return current;

      if ( IsInRoot( current, root ) )
        return current;

      var candidates = root.GetComponentsInChildren<T>( true );
      if ( candidates == null || candidates.Length == 0 )
        return null;

      foreach ( var candidate in candidates ) {
        if ( IsInRoot( candidate, root ) )
          return candidate;
      }

      return null;
    }

    public static Transform ResolveMachineRoot( Component context, Transform current )
    {
      if ( IsSelectable( current ) )
        return current;

      var catExcavator = ResolveActiveComponent<Excavator>( context, null );
      var e85Excavator = ResolveActiveComponent<global::ExcavatorE85>( context, null );
      var yuLongExcavator = ResolveActiveComponent<ExcavatorYuLong>( context, null );
      var machineComponent = ChooseMachineComponent( context != null ? context.transform : null,
                                                     catExcavator,
                                                     e85Excavator,
                                                     yuLongExcavator );
      return machineComponent != null ? machineComponent.transform : null;
    }

    public static Transform ResolveMachineRoot( Component context, Transform current, Transform explicitRoot )
    {
      if ( IsSelectable( explicitRoot ) )
        return explicitRoot;

      return ResolveMachineRoot( context, current );
    }

    public static Transform ResolveBucketReference( Transform excavatorRoot, Transform current )
    {
      return ResolveSemanticChild( excavatorRoot, current, DefaultBucketSemanticNames );
    }

    public static Transform ResolveSemanticChild( Transform root, Transform current, params string[] semanticNames )
    {
      if ( root == null )
        return IsSelectable( current ) ? current : null;

      if ( IsSelectable( current ) && current.IsChildOf( root ) )
        return current;

      var exactMatch = FindChildRecursive( root, semanticNames, exact: true );
      if ( exactMatch != null )
        return exactMatch;

      var semanticMatch = FindChildRecursive( root, semanticNames, exact: false );
      if ( semanticMatch != null )
        return semanticMatch;

      return null;
    }

    public static bool IsSelectable( Component component )
    {
      return component != null && IsSelectable( component.gameObject );
    }

    public static bool IsSelectable( Transform transform )
    {
      return transform != null && IsSelectable( transform.gameObject );
    }

    public static bool IsSelectableInRoot( Component component, Transform root )
    {
      return IsSelectable( component ) && IsInRoot( component, root );
    }

    public static bool IsSelectableInRoot( Transform transform, Transform root )
    {
      return IsSelectable( transform ) && IsInRoot( transform, root );
    }

    public static bool IsInRoot( Component component, Transform root )
    {
      return component != null && IsInRoot( component.transform, root );
    }

    public static bool IsInRoot( Transform transform, Transform root )
    {
      if ( transform == null || root == null )
        return false;

      return transform == root || transform.IsChildOf( root );
    }

    private static bool IsSelectable( GameObject gameObject )
    {
      return gameObject != null && gameObject.activeInHierarchy;
    }

    private static bool ShouldKeepCurrent<T>( T current ) where T : Component
    {
      if ( current == null )
        return false;

      if ( IsSelectable( current ) )
        return true;

      return FindActiveInScene<T>() == null;
    }

    private static T FindActiveInScene<T>() where T : Component
    {
      var candidates = Object.FindObjectsByType<T>( FindObjectsInactive.Include, FindObjectsSortMode.None );
      if ( candidates == null || candidates.Length == 0 )
        return null;

      foreach ( var candidate in candidates ) {
        if ( IsSelectable( candidate ) )
          return candidate;
      }

      return null;
    }

    private static T FindBestActiveInScene<T>( Transform contextTransform ) where T : Component
    {
      var candidates = Object.FindObjectsByType<T>( FindObjectsInactive.Include, FindObjectsSortMode.None );
      if ( candidates == null || candidates.Length == 0 )
        return null;

      T bestCandidate = null;
      var bestScore = float.PositiveInfinity;
      for ( var index = 0; index < candidates.Length; ++index ) {
        var candidate = candidates[ index ];
        if ( !IsSelectable( candidate ) )
          continue;

        var score = ScoreCandidate( contextTransform, candidate.transform );
        if ( score < bestScore ) {
          bestCandidate = candidate;
          bestScore = score;
        }
      }

      return bestCandidate;
    }

    private static Component ChooseMachineComponent( Transform contextTransform,
                                                     Excavator catExcavator,
                                                     global::ExcavatorE85 e85Excavator,
                                                     ExcavatorYuLong yuLongExcavator )
    {
      Component bestComponent = null;
      var bestScore = float.PositiveInfinity;

      ConsiderMachineCandidate( contextTransform, catExcavator, ref bestComponent, ref bestScore );
      ConsiderMachineCandidate( contextTransform, e85Excavator, ref bestComponent, ref bestScore );
      ConsiderMachineCandidate( contextTransform, yuLongExcavator, ref bestComponent, ref bestScore );

      return bestComponent;
    }

    private static void ConsiderMachineCandidate( Transform contextTransform,
                                                  Component candidate,
                                                  ref Component bestComponent,
                                                  ref float bestScore )
    {
      if ( candidate == null )
        return;

      var score = ScoreCandidate( contextTransform, candidate.transform );
      if ( score > bestScore )
        return;

      bestComponent = candidate;
      bestScore = score;
    }

    private static T FindBestInScene<T>( Transform contextTransform, T fallback ) where T : Component
    {
      var candidates = Object.FindObjectsByType<T>( FindObjectsInactive.Include, FindObjectsSortMode.None );
      if ( candidates == null || candidates.Length == 0 )
        return fallback;

      T bestCandidate = null;
      var bestScore = float.PositiveInfinity;
      for ( var index = 0; index < candidates.Length; ++index ) {
        var candidate = candidates[ index ];
        if ( candidate == null )
          continue;

        var score = ScoreCandidate( contextTransform, candidate.transform );
        if ( score < bestScore ) {
          bestCandidate = candidate;
          bestScore = score;
        }
      }

      return bestCandidate != null ? bestCandidate : fallback;
    }

    private static float ScoreCandidate( Transform contextTransform, Transform candidateTransform )
    {
      if ( candidateTransform == null )
        return float.PositiveInfinity;

      var score = IsSelectable( candidateTransform ) ? 0.0f : 1000000.0f;
      if ( contextTransform == null )
        return score;

      if ( candidateTransform.root == contextTransform.root )
        return score;

      return score + 1000.0f + Vector3.SqrMagnitude( candidateTransform.position - contextTransform.position );
    }

    private static Transform FindChildRecursive( Transform root, string[] semanticNames, bool exact )
    {
      if ( root == null )
        return null;

      if ( IsSelectable( root ) && MatchesAnySemanticName( root.name, semanticNames, exact ) )
        return root;

      foreach ( Transform child in root ) {
        var result = FindChildRecursive( child, semanticNames, exact );
        if ( result != null )
          return result;
      }

      return null;
    }

    private static bool MatchesAnySemanticName( string candidateName, string[] semanticNames, bool exact )
    {
      if ( string.IsNullOrWhiteSpace( candidateName ) || semanticNames == null )
        return false;

      var normalizedCandidate = NormalizeName( candidateName );
      foreach ( var semanticName in semanticNames ) {
        if ( string.IsNullOrWhiteSpace( semanticName ) )
          continue;

        var normalizedSemantic = NormalizeName( semanticName );
        if ( exact ) {
          if ( normalizedCandidate == normalizedSemantic )
            return true;
        }
        else if ( normalizedCandidate.Contains( normalizedSemantic ) ) {
          return true;
        }
      }

      return false;
    }

    private static string NormalizeName( string value )
    {
      if ( string.IsNullOrWhiteSpace( value ) )
        return string.Empty;

      return value.Replace( " ", string.Empty )
                  .Replace( "_", string.Empty )
                  .Replace( "-", string.Empty )
                  .ToLowerInvariant();
    }
  }
}
