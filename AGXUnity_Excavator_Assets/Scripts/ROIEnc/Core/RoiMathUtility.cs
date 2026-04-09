using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Core
{
  public static class RoiMathUtility
  {
    public static Rect ClampNormalizedRectTopLeft( Rect rect )
    {
      var xMin = Mathf.Clamp01( rect.xMin );
      var yMin = Mathf.Clamp01( rect.yMin );
      var xMax = Mathf.Clamp01( rect.xMax );
      var yMax = Mathf.Clamp01( rect.yMax );

      return Rect.MinMaxRect(
        Mathf.Min( xMin, xMax ),
        Mathf.Min( yMin, yMax ),
        Mathf.Max( xMin, xMax ),
        Mathf.Max( yMin, yMax ) );
    }

    public static float IntersectionOverUnion( Rect left, Rect right )
    {
      var xMin = Mathf.Max( left.xMin, right.xMin );
      var yMin = Mathf.Max( left.yMin, right.yMin );
      var xMax = Mathf.Min( left.xMax, right.xMax );
      var yMax = Mathf.Min( left.yMax, right.yMax );

      if ( xMax <= xMin || yMax <= yMin )
        return 0.0f;

      var intersection = ( xMax - xMin ) * ( yMax - yMin );
      var union = left.width * left.height + right.width * right.height - intersection;
      return union > 1.0e-6f ? intersection / union : 0.0f;
    }

    public static Rect LerpRect( Rect from, Rect to, float alpha )
    {
      return new Rect(
        Mathf.Lerp( from.x, to.x, alpha ),
        Mathf.Lerp( from.y, to.y, alpha ),
        Mathf.Lerp( from.width, to.width, alpha ),
        Mathf.Lerp( from.height, to.height, alpha ) );
    }
  }
}
