using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Control.Core
{
  public static class ExcavatorRigLocator
  {
    public static T ResolveComponent<T>( Component context, T current ) where T : Component
    {
      if ( current != null )
        return current;

      if ( context == null )
        return FindBestInScene<T>( null );

      var localComponent = context.GetComponent<T>();
      if ( localComponent != null )
        return localComponent;

      var parentComponent = context.GetComponentInParent<T>( true );
      if ( parentComponent != null )
        return parentComponent;

      var childComponent = context.GetComponentInChildren<T>( true );
      if ( childComponent != null )
        return childComponent;

      return FindBestInScene<T>( context.transform );
    }

    private static T FindBestInScene<T>( Transform contextTransform ) where T : Component
    {
      var candidates = Object.FindObjectsByType<T>( FindObjectsInactive.Include, FindObjectsSortMode.None );
      if ( candidates == null || candidates.Length == 0 )
        return null;

      var bestCandidate = candidates[ 0 ];
      var bestScore = ScoreCandidate( contextTransform, bestCandidate.transform );
      for ( var index = 1; index < candidates.Length; ++index ) {
        var candidate = candidates[ index ];
        var score = ScoreCandidate( contextTransform, candidate.transform );
        if ( score < bestScore ) {
          bestCandidate = candidate;
          bestScore = score;
        }
      }

      return bestCandidate;
    }

    private static float ScoreCandidate( Transform contextTransform, Transform candidateTransform )
    {
      if ( candidateTransform == null )
        return float.PositiveInfinity;

      if ( contextTransform == null )
        return 0.0f;

      if ( candidateTransform.root == contextTransform.root )
        return 0.0f;

      return 1000.0f + Vector3.SqrMagnitude( candidateTransform.position - contextTransform.position );
    }
  }
}
