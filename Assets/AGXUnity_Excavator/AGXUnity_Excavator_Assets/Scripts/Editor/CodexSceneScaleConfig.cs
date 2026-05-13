#if UNITY_EDITOR
using System.Globalization;

namespace AGXUnity_Excavator.Scripts.Editor
{
  internal static class CodexSceneScaleConfig
  {
    public const string ProjectName = "AGXUnityE85ExcavatorSim";
    public const string MainScenePath = "Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity";

    public const float LegacyEnvironmentScale = 1.25f;
    public const float DefaultEnvironmentScale = 1.5f;

    public const float BaseRoomSizeX = 7.0f;
    public const float BaseRoomSizeZ = 7.0f;
    public const float BaseRoomHeight = 3.5f;
    public const float BaseBoardThickness = 0.05f;
    public const float BaseAreaSizeX = 2.5f;
    public const float BaseAreaSizeZ = 3.0f;
    public const float BaseAreaHeight = 0.7f;
    public const float BaseDigSoilHeight = 0.6f;
    public const float BaseTerrainVerticalScale = 1.4f;
    public const float BaseTerrainMaximumDepth = 0.7f;
    public const float BaseExcavatorCenterlineZ = 5.1f;
    public const float BaseExcavatorBoomBaseX = 3.2f;
    public const float BaseDumpMinX = 0.7f;
    public const float BaseDumpMinZ = 0.2f;
    public const float BaseDigMinX = 4.0f;
    public const float BaseDigMinZ = 3.6f;

    public const float RoomSizeX = BaseRoomSizeX * DefaultEnvironmentScale;
    public const float RoomSizeZ = BaseRoomSizeZ * DefaultEnvironmentScale;
    public const float RoomHeight = BaseRoomHeight * DefaultEnvironmentScale;
    public const float BoardThickness = BaseBoardThickness * DefaultEnvironmentScale;
    public const float AreaSizeX = BaseAreaSizeX * DefaultEnvironmentScale;
    public const float AreaSizeZ = BaseAreaSizeZ * DefaultEnvironmentScale;
    public const float AreaHeight = BaseAreaHeight * DefaultEnvironmentScale;
    public const float DigSoilHeight = BaseDigSoilHeight * DefaultEnvironmentScale;
    public const float TerrainVerticalScale = BaseTerrainVerticalScale * DefaultEnvironmentScale;
    public const float TerrainMaximumDepth = BaseTerrainMaximumDepth * DefaultEnvironmentScale;
    public const float ExcavatorCenterlineZ = BaseExcavatorCenterlineZ * DefaultEnvironmentScale;
    public const float ExcavatorBoomBaseX = BaseExcavatorBoomBaseX * DefaultEnvironmentScale;

    public static string FormatScale( float scale )
    {
      return scale.ToString( "0.###", CultureInfo.InvariantCulture );
    }

    public static string FormatScaleForFileName( float scale )
    {
      return FormatScale( scale ).Replace( ".", "p" );
    }
  }
}
#endif
