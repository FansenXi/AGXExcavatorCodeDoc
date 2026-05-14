#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Editor
{
  internal static class CodexAreaFootprintUtility
  {
    public struct TerrainSpec
    {
      public bool FromFootprint;
      public Vector3 FootprintCenter;
      public Vector2 FootprintSizeXZ;
      public float FootprintHeight;
      public float FootprintBottomY;
      public float BoardThickness;
      public float AreaHeight;
      public Vector2 InnerSizeXZ;
      public Vector3 TerrainWorldMin;

      public Vector3 TerrainSize =>
        new Vector3( InnerSizeXZ.x, CodexSceneScaleConfig.TerrainVerticalScale, InnerSizeXZ.y );

      public Vector3 FullAreaHalfExtents =>
        new Vector3( FootprintSizeXZ.x * 0.5f, AreaHeight * 0.5f, FootprintSizeXZ.y * 0.5f );
    }

    public static TerrainSpec ResolveTerrainSpec( string areaName,
                                                  Vector2 fallbackMinXZ,
                                                  IList<string> warnings = null )
    {
      var footprintSize = new Vector2( CodexSceneScaleConfig.AreaSizeX,
                                       CodexSceneScaleConfig.AreaSizeZ );
      var footprintCenter = new Vector3( fallbackMinXZ.x + footprintSize.x * 0.5f,
                                         0.0f,
                                         fallbackMinXZ.y + footprintSize.y * 0.5f );
      var footprintHeight = 0.03f * CodexSceneScaleConfig.DefaultEnvironmentScale;
      var footprintBottomY = 0.0f;
      var fromFootprint = false;

      var footprint = FindSceneObject( areaName + "_Footprint" );
      if ( footprint != null ) {
        var footprintScale = Abs( footprint.transform.lossyScale );
        footprintSize = new Vector2( footprintScale.x, footprintScale.z );
        footprintHeight = footprintScale.y;
        footprintCenter = footprint.transform.position;
        footprintBottomY = footprintCenter.y - footprintScale.y * 0.5f;
        fromFootprint = true;
      }
      else {
        warnings?.Add( $"{areaName}_Footprint was not found; using configured fallback area size." );
      }

      var boardThickness = ResolveBoardThickness( areaName, warnings );
      var areaHeight = ResolveAreaHeight( areaName );
      var innerSize = new Vector2( footprintSize.x - 2.0f * boardThickness,
                                   footprintSize.y - 2.0f * boardThickness );
      if ( innerSize.x <= 0.0f || innerSize.y <= 0.0f ) {
        warnings?.Add( $"{areaName} footprint is too small for board thickness; using configured fallback inner size." );
        innerSize = new Vector2( CodexSceneScaleConfig.AreaSizeX - 2.0f * CodexSceneScaleConfig.BoardThickness,
                                 CodexSceneScaleConfig.AreaSizeZ - 2.0f * CodexSceneScaleConfig.BoardThickness );
        boardThickness = CodexSceneScaleConfig.BoardThickness;
      }

      return new TerrainSpec {
        FromFootprint = fromFootprint,
        FootprintCenter = footprintCenter,
        FootprintSizeXZ = footprintSize,
        FootprintHeight = footprintHeight,
        FootprintBottomY = footprintBottomY,
        BoardThickness = boardThickness,
        AreaHeight = areaHeight,
        InnerSizeXZ = innerSize,
        TerrainWorldMin = new Vector3( footprintCenter.x - footprintSize.x * 0.5f + boardThickness,
                                       footprintBottomY,
                                       footprintCenter.z - footprintSize.y * 0.5f + boardThickness )
      };
    }

    public static GameObject FindSceneObject( string objectName )
    {
      var objects = Resources.FindObjectsOfTypeAll<GameObject>();
      foreach ( var candidate in objects ) {
        if ( candidate == null || candidate.name != objectName || !candidate.scene.IsValid() )
          continue;

        return candidate;
      }

      return null;
    }

    public static GameObject FindMassSensorObjectByTargetName( string targetName )
    {
      var sensors = Resources.FindObjectsOfTypeAll<global::TerrainParticleBoxMassSensor>();
      foreach ( var sensor in sensors ) {
        if ( sensor == null || !sensor.gameObject.scene.IsValid() )
          continue;
        if ( string.Equals( sensor.TargetName, targetName, StringComparison.OrdinalIgnoreCase ) )
          return sensor.gameObject;
      }

      return null;
    }

    private static float ResolveBoardThickness( string areaName, IList<string> warnings )
    {
      var values = new List<float>( 4 );
      AddScaleComponent( values, areaName + "_XMin_Board", Axis.X );
      AddScaleComponent( values, areaName + "_XMax_Board", Axis.X );
      AddScaleComponent( values, areaName + "_ZMin_Board", Axis.Z );
      AddScaleComponent( values, areaName + "_ZMax_Board", Axis.Z );

      if ( values.Count == 0 ) {
        warnings?.Add( $"{areaName} board thickness could not be inferred; using configured fallback thickness." );
        return CodexSceneScaleConfig.BoardThickness;
      }

      return Average( values );
    }

    private static float ResolveAreaHeight( string areaName )
    {
      var values = new List<float>( 4 );
      AddScaleComponent( values, areaName + "_XMin_Board", Axis.Y );
      AddScaleComponent( values, areaName + "_XMax_Board", Axis.Y );
      AddScaleComponent( values, areaName + "_ZMin_Board", Axis.Y );
      AddScaleComponent( values, areaName + "_ZMax_Board", Axis.Y );

      return values.Count > 0 ? Average( values ) : CodexSceneScaleConfig.AreaHeight;
    }

    private static void AddScaleComponent( List<float> values, string objectName, Axis axis )
    {
      var gameObject = FindSceneObject( objectName );
      if ( gameObject == null )
        return;

      var scale = Abs( gameObject.transform.lossyScale );
      var value = axis == Axis.X ? scale.x : axis == Axis.Y ? scale.y : scale.z;
      if ( value > 1.0e-5f )
        values.Add( value );
    }

    private static float Average( List<float> values )
    {
      var total = 0.0f;
      foreach ( var value in values )
        total += value;

      return total / values.Count;
    }

    private static Vector3 Abs( Vector3 value )
    {
      return new Vector3( Mathf.Abs( value.x ), Mathf.Abs( value.y ), Mathf.Abs( value.z ) );
    }

    private enum Axis
    {
      X,
      Y,
      Z
    }
  }
}
#endif
