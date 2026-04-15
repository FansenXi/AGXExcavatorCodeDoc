using System;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Core
{
  public enum RoiCategory
  {
    Unknown = 0,
    Bucket = 1,
    ExcavatorArm = 2,
    Truck = 3,
    Container = 4,
    DigArea = 5
  }

  public enum RoiSource
  {
    Unknown = 0,
    VisualModel = 1,
    SceneGraphLabel = 2,
    KinematicFallback = 3,
    MotionEstimator = 4,
    Fusion = 5,
    RuleRoi = 6
  }

  public enum RoiDetectionExperimentGroup
  {
    VisualOnly = 0,
    VisualPlusMotionIntensity = 1,
    VisualPlusKinematicFallback = 2,
    VisualPlusAllSignals = 3
  }

  public static class RoiCategoryUtility
  {
    public static string ToLabel( RoiCategory category )
    {
      switch ( category ) {
        case RoiCategory.Bucket:
          return "bucket";
        case RoiCategory.ExcavatorArm:
          return "excavator_arm";
        case RoiCategory.Truck:
          return "truck";
        case RoiCategory.Container:
          return "container";
        case RoiCategory.DigArea:
          return "dig_area";
        default:
          return "unknown";
      }
    }

    public static RoiCategory FromLabel( string label )
    {
      if ( string.IsNullOrWhiteSpace( label ) )
        return RoiCategory.Unknown;

      switch ( label.Trim().ToLowerInvariant() ) {
        case "bucket":
          return RoiCategory.Bucket;
        case "excavator_arm":
        case "arm":
          return RoiCategory.ExcavatorArm;
        case "truck":
        case "truck_bed":
          return RoiCategory.Truck;
        case "container":
        case "container_box":
          return RoiCategory.Container;
        case "dig_area":
          return RoiCategory.DigArea;
        default:
          return RoiCategory.Unknown;
      }
    }

    public static Color ColorFor( RoiCategory category )
    {
      switch ( category ) {
        case RoiCategory.Bucket:
          return new Color( 1.0f, 0.72f, 0.22f, 1.0f );
        case RoiCategory.ExcavatorArm:
          return new Color( 0.36f, 0.82f, 1.0f, 1.0f );
        case RoiCategory.Truck:
          return new Color( 0.45f, 1.0f, 0.45f, 1.0f );
        case RoiCategory.Container:
          return new Color( 1.0f, 0.42f, 0.42f, 1.0f );
        case RoiCategory.DigArea:
          return new Color( 1.0f, 0.52f, 0.12f, 1.0f );
        default:
          return Color.white;
      }
    }
  }
}
