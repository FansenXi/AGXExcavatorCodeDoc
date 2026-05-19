using agx;
using AGXUnity.Collide;
using AGXUnity.Utils;
using System;
using UnityEngine;

namespace AGXUnity.Model
{
  [AddComponentMenu( "AGXUnity/Model/Deformable Terrain" )]
  [RequireComponent( typeof( Terrain ) )]
  [DisallowMultipleComponent]
  [HelpURL( "https://us.download.algoryx.se/AGXUnity/documentation/current/editor_interface.html#deformable-terrain" )]
  public class DeformableTerrain : DeformableTerrainBase
  {
    /// <summary>
    /// Native deformable terrain instance - accessible after this
    /// component has been initialized and is valid.
    /// </summary>
    public agxTerrain.Terrain Native { get; private set; } = null;

    /// <summary>
    /// Unity Terrain component.
    /// </summary>
    public Terrain Terrain
    {
      get
      {
        return m_terrain == null ?
                 m_terrain = GetComponent<Terrain>() :
                 m_terrain;
      }
    }

    /// <summary>
    /// Unity Terrain data.
    /// </summary>
    [HideInInspector]
    public TerrainData TerrainData { get { return Terrain?.terrainData; } }

    [HideInInspector]
    public int TerrainDataResolution { get { return TerrainUtils.TerrainDataResolution( TerrainData ); } }

    /// <summary>
    /// The compaction that all terrain cells are initialized to.
    /// </summary>
    [DisableInRuntimeInspector]
    [ClampAboveZeroInInspector( true )]
    [InspectorPriority( -1 )]
    [field: SerializeField]
    [Tooltip( "The compaction that all terrain cells are initialized to." )]
    public float InitialCompaction { get; set; } = 1.0f;

    /// <summary>
    /// Resets heights of the Unity terrain and recreate native instance.
    /// </summary>
    public void ResetHeights()
    {
      ResetTerrainDataHeightsAndTransform();

      var nativeHeightData = TerrainUtils.WriteTerrainDataOffset( Terrain, MaximumDepth );
      ApplyMaximumDepthTransformOffset();

      Native.setHeights( nativeHeightData.Heights );

      if ( InitialCompaction != 1.0f )
        Native.setCompaction( InitialCompaction, true );

      PropertySynchronizer.Synchronize( this );
    }

    /// <summary>
    /// Restores the initial Unity terrain data and recreates the native terrain
    /// instance so any dynamic soil mass/particles are cleared as part of reset.
    /// </summary>
    public void ResetHeightsAndRecreateNative()
    {
      if ( TerrainProperties != null )
        TerrainProperties.Unregister( this );

      if ( Simulation.HasInstance && Native != null )
        GetSimulation()?.remove( Native );

      Native = null;

      ResetTerrainDataHeightsAndTransform();
      InitializeNative();

      PropertySynchronizer.Synchronize( this );
      SetEnable( isActiveAndEnabled );

      // Terrain-dependent renderers/material overlays subscribe to terrain
      // modification callbacks, so force a full refresh after the native reset.
      TriggerModifyAllCells();
    }

    /// <summary>
    /// If, e.g., OnDestroy wasn't called to reset the heights of the
    /// terrain this method can recover some data of the previous terrain
    /// height data. This method will subtract MaximumDepth from each
    /// entry in terrain data.
    /// </summary>
    public void PatchTerrainData()
    {
      TerrainUtils.WriteTerrainDataOffset( Terrain, -MaximumDepth );
    }

    protected override bool Initialize()
    {
      // Only printing the errors if something is wrong.
      LicenseManager.LicenseInfo.HasModuleLogError( LicenseInfo.Module.AGXTerrain | LicenseInfo.Module.AGXGranular, this );

#if UNITY_EDITOR
      UsePlayModeTerrainDataInstance();
      RegisterPlayModeSceneSaveGuard();
#endif

      m_initialHeights = TerrainData.GetHeights( 0, 0, TerrainDataResolution, TerrainDataResolution );

      InitializeNative();

      Simulation.Instance.StepCallbacks.PostStepForward += OnPostStepForward;

      // Native terrain may change the number of PPGS iterations to default (25).
      // Override if we have solver settings set to the simulation.
      if ( Simulation.Instance.SolverSettings != null )
        GetSimulation().getSolver().setNumPPGSRestingIterations( (ulong)Simulation.Instance.SolverSettings.PpgsRestingIterations );

      SetEnable( isActiveAndEnabled );

      return true;
    }

    protected override void OnDestroy()
    {
      ResetTerrainDataHeightsAndTransform();

      if ( TerrainProperties != null )
        TerrainProperties.Unregister( this );

      if ( Simulation.HasInstance ) {
        GetSimulation().remove( Native );
        Simulation.Instance.StepCallbacks.PostStepForward -= OnPostStepForward;
      }
      Native = null;

#if UNITY_EDITOR
      UnregisterPlayModeSceneSaveGuard();
      RestorePlayModeTerrainDataInstance();
#endif

      base.OnDestroy();
    }

    private void InitializeNative()
    {
      var nativeHeightData = TerrainUtils.WriteTerrainDataOffset( Terrain, MaximumDepth );

      ApplyMaximumDepthTransformOffset();

      Native = new agxTerrain.Terrain( (uint)nativeHeightData.ResolutionX,
                                       (uint)nativeHeightData.ResolutionY,
                                       ElementSize,
                                       nativeHeightData.Heights,
                                       false,
                                       0.0f );

      Native.setTransform( Utils.TerrainUtils.CalculateNativeOffset( transform, TerrainData ) );

      if ( InitialCompaction != 1.0f )
        Native.setCompaction( InitialCompaction );

      GetSimulation().add( Native );
    }

    private void ResetTerrainDataHeightsAndTransform()
    {
      if ( m_initialHeights == null )
        return;

      TerrainData.SetHeights( 0, 0, m_initialHeights );
      RestoreMaximumDepthTransformOffset();

#if UNITY_EDITOR
      // Runtime terrain offsets are temporary AGX visualization state; saving
      // them while playing can persist MaximumDepth into the TerrainData asset.
      if ( !Application.isPlaying ) {
        UnityEditor.EditorUtility.SetDirty( TerrainData );
        UnityEditor.AssetDatabase.SaveAssets();
      }
#endif
    }

    private void OnPostStepForward()
    {
      if ( Native == null )
        return;

      UpdateHeights( Native.getModifiedVertices() );
    }

    private void UpdateHeights( agxTerrain.ModifiedVerticesVector modifiedVertices )
    {
      if ( modifiedVertices.Count == 0 )
        return;

      var scale  = TerrainData.heightmapScale.y;
      var resX   = TerrainDataResolution;
      var resY   = TerrainDataResolution;
      var result = new float[,] { { 0.0f } };
      foreach ( var index in modifiedVertices ) {
        var unityIndex = new Vector2Int((int)(resX - index.x - 1), (int)(resY - index.y - 1));
        var h = (float)Native.getHeight( index );

        result[ 0, 0 ] = h / scale;

        TerrainData.SetHeightsDelayLOD( unityIndex.x, unityIndex.y, result );
        OnModification?.Invoke( Native, index, Terrain, unityIndex );
      }

      TerrainData.SyncHeightmap();
    }

    private void ApplyMaximumDepthTransformOffset()
    {
#if UNITY_EDITOR
      if ( Application.isPlaying && !m_hasPlayModeTransformOffset ) {
        m_playModeTransformPositionBeforeOffset = transform.position;
        m_hasPlayModeTransformOffset = true;
      }
#endif

      transform.position = transform.position + MaximumDepth * Vector3.down;
    }

    private void RestoreMaximumDepthTransformOffset()
    {
#if UNITY_EDITOR
      if ( Application.isPlaying && m_hasPlayModeTransformOffset ) {
        transform.position = m_playModeTransformPositionBeforeOffset;
        m_hasPlayModeTransformOffset = false;
        return;
      }
#endif

      transform.position = transform.position + MaximumDepth * Vector3.up;
    }

#if UNITY_EDITOR
    private void UsePlayModeTerrainDataInstance()
    {
      if ( !Application.isPlaying || Terrain == null || Terrain.terrainData == null || m_playModeTerrainData != null )
        return;

      m_playModeOriginalTerrainData = Terrain.terrainData;
      m_playModeTerrainData = Instantiate( m_playModeOriginalTerrainData );
      m_playModeTerrainData.name = m_playModeOriginalTerrainData.name + " (Play Mode)";
      m_playModeTerrainData.hideFlags = HideFlags.DontSave;
      AssignTerrainData( m_playModeTerrainData );
    }

    private void RestorePlayModeTerrainDataInstance()
    {
      if ( m_playModeOriginalTerrainData != null )
        AssignTerrainData( m_playModeOriginalTerrainData );

      if ( m_playModeTerrainData != null )
        DestroyImmediate( m_playModeTerrainData );

      m_playModeOriginalTerrainData = null;
      m_playModeTerrainData = null;
      m_playModeTerrainDataWasPreparedForSceneSave = false;
    }

    private void AssignTerrainData( TerrainData terrainData )
    {
      if ( Terrain != null )
        Terrain.terrainData = terrainData;

      var terrainCollider = GetComponent<TerrainCollider>();
      if ( terrainCollider != null )
        terrainCollider.terrainData = terrainData;
    }

    private void RegisterPlayModeSceneSaveGuard()
    {
      if ( !Application.isPlaying || m_isPlayModeSceneSaveGuardRegistered )
        return;

      UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnEditorSceneSaving;
      UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnEditorSceneSaved;
      m_isPlayModeSceneSaveGuardRegistered = true;
    }

    private void UnregisterPlayModeSceneSaveGuard()
    {
      if ( !m_isPlayModeSceneSaveGuardRegistered )
        return;

      UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnEditorSceneSaving;
      UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnEditorSceneSaved;
      m_isPlayModeSceneSaveGuardRegistered = false;
    }

    private void OnEditorSceneSaving( UnityEngine.SceneManagement.Scene scene, string path )
    {
      if ( this == null || gameObject == null || gameObject.scene != scene )
        return;

      PreparePlayModeStateForSceneSave();
    }

    private void OnEditorSceneSaved( UnityEngine.SceneManagement.Scene scene )
    {
      if ( this == null || gameObject == null || gameObject.scene != scene )
        return;

      RestorePlayModeStateAfterSceneSave();
    }

    private void PreparePlayModeStateForSceneSave()
    {
      if ( !Application.isPlaying )
        return;

      if ( m_hasPlayModeTransformOffset ) {
        transform.position = m_playModeTransformPositionBeforeOffset;
        m_playModeTransformWasPreparedForSceneSave = true;
      }

      if ( m_playModeOriginalTerrainData != null && Terrain != null && Terrain.terrainData == m_playModeTerrainData ) {
        AssignTerrainData( m_playModeOriginalTerrainData );
        m_playModeTerrainDataWasPreparedForSceneSave = true;
      }
    }

    private void RestorePlayModeStateAfterSceneSave()
    {
      if ( !Application.isPlaying )
        return;

      if ( m_playModeTerrainDataWasPreparedForSceneSave && m_playModeTerrainData != null )
        AssignTerrainData( m_playModeTerrainData );

      if ( m_playModeTransformWasPreparedForSceneSave && m_hasPlayModeTransformOffset )
        transform.position = m_playModeTransformPositionBeforeOffset + MaximumDepth * Vector3.down;

      m_playModeTransformWasPreparedForSceneSave = false;
      m_playModeTerrainDataWasPreparedForSceneSave = false;
    }
#endif

    private Terrain m_terrain = null;
    private float[,] m_initialHeights = null;

#if UNITY_EDITOR
    private TerrainData m_playModeOriginalTerrainData = null;
    private TerrainData m_playModeTerrainData = null;
    private Vector3 m_playModeTransformPositionBeforeOffset = Vector3.zero;
    private bool m_hasPlayModeTransformOffset = false;
    private bool m_isPlayModeSceneSaveGuardRegistered = false;
    private bool m_playModeTransformWasPreparedForSceneSave = false;
    private bool m_playModeTerrainDataWasPreparedForSceneSave = false;
#endif

    // -----------------------------------------------------------------------------------------------------------
    // ------------------------------- Implementation of DeformableTerrainBase -----------------------------------
    // -----------------------------------------------------------------------------------------------------------
    [DisableInRuntimeInspector]
    public override float ElementSize => TerrainData.size.x / ( TerrainDataResolution - 1 );
    public override agx.GranularBodyPtrArray GetParticles() { return Native?.getSoilSimulationInterface()?.getSoilParticles(); }
    public override Uuid GetParticleMaterialUuid() => Native?.getMaterial( agxTerrain.Terrain.MaterialType.PARTICLE ).getUuid();
    public override agxTerrain.SoilSimulationInterface GetSoilSimulationInterface() { return Native?.getSoilSimulationInterface(); }
    public override agxTerrain.TerrainProperties GetProperties() { return Native?.getProperties(); }

    public override void ConvertToDynamicMassInShape( Shape failureVolume )
    {
      if ( !IsNativeNull() )
        Native.convertToDynamicMassInShape( failureVolume.GetInitialized<Shape>().NativeShape );
    }

    public override void SetHeights( int xstart, int ystart, float[,] heights )
    {
      int height = heights.GetLength(0);
      int width = heights.GetLength(1);
      int resolution = TerrainDataResolution;

      if ( xstart + width >= resolution || xstart < 0 || ystart + height >= resolution || ystart < 0 )
        throw new ArgumentOutOfRangeException( "", $"Provided height patch with start ({xstart},{ystart}) and size ({width},{height}) extends outside of the terrain bounds [0,{TerrainDataResolution - 1}]" );

      float scale = TerrainData.size.y;
      float depthOffset = 0;
      if ( Native != null )
        depthOffset = MaximumDepth;

      for ( int y = 0; y < height; y++ ) {
        for ( int x = 0; x < width; x++ ) {
          float value = heights[ y, x ] + depthOffset;
          heights[ y, x ] = value / scale;

          agx.Vec2i idx = new agx.Vec2i( resolution - 1 - x - xstart, resolution - 1 - y - ystart);
          Native?.setHeight( idx, value );
        }
      }

      TerrainData.SetHeights( xstart, ystart, heights );
    }
    public override void SetHeight( int x, int y, float height )
    {
      if ( x >= TerrainDataResolution || x < 0 || y >= TerrainDataResolution || y < 0 )
        throw new ArgumentOutOfRangeException( "(x, y)", $"Indices ({x},{y}) is outside of the terrain bounds [0,{TerrainDataResolution - 1}]" );

      if ( Native != null )
        height += MaximumDepth;

      agx.Vec2i idx = new agx.Vec2i( TerrainDataResolution - 1 - x, TerrainDataResolution - 1 - y );
      Native?.setHeight( idx, height );

      TerrainData.SetHeights( x, y, new float[,] { { height / TerrainData.size.y } } );
    }
    public override float[,] GetHeights( int xstart, int ystart, int width, int height )
    {
      if ( width <= 0 || height <= 0 )
        throw new ArgumentOutOfRangeException( "width, height", $"Width and height ({width} / {height}) must be greater than 0" );

      int resolution = TerrainDataResolution;

      if ( xstart + width >= resolution || xstart < 0 || ystart + height >= resolution || ystart < 0 )
        throw new ArgumentOutOfRangeException( "", $"Requested height patch with start ({xstart},{ystart}) and size ({width},{height}) extends outside of the terrain bounds [0,{TerrainDataResolution - 1}]" );

      float scale = TerrainData.size.y;
      float [,] heights;
      if ( Native == null ) {
        heights = TerrainData.GetHeights( xstart, ystart, width, height );
        for ( int y = 0; y < height; y++ ) {
          for ( int x = 0; x < width; x++ ) {
            heights[ y, x ] = heights[ y, x ] * scale;
          }
        }
        return heights;
      }

      heights = new float[ height, width ];
      for ( int y = 0; y < height; y++ ) {
        for ( int x = 0; x < width; x++ ) {
          agx.Vec2i idx = new agx.Vec2i( resolution - 1 - x - xstart, resolution - 1 - y - ystart);
          heights[ y, x ] = (float)Native.getHeight( idx ) - MaximumDepth;
        }
      }
      return heights;
    }
    public override float GetHeight( int x, int y )
    {
      if ( x >= TerrainDataResolution || x < 0 || y >= TerrainDataResolution || y < 0 )
        throw new ArgumentOutOfRangeException( "(x, y)", $"Indices ({x},{y}) is outside of the terrain bounds [0,{TerrainDataResolution - 1}]" );

      if ( Native == null )
        return TerrainData.GetHeight( x, y );

      agx.Vec2i idx = new agx.Vec2i( TerrainDataResolution - 1 - x, TerrainDataResolution - 1 - y );
      return (float)Native.getHeight( idx ) - MaximumDepth;
    }

    public override void TriggerModifyAllCells()
    {
      var res = TerrainDataResolution;
      var agxIdx = new agx.Vec2i( 0, 0 );
      var uTerr = Terrain;
      var uIdx = new Vector2Int( 0, 0 );
      for ( int y = 0; y < res; y++ ) {
        agxIdx.y = res - 1 - y;
        uIdx.y = y;
        for ( int x = 0; x < res; x++ ) {
          agxIdx.x = res - 1 - x;
          uIdx.x = x;
          OnModification?.Invoke( Native, agxIdx, uTerr, uIdx );
        }
      }
    }

    public override bool ReplaceTerrainMaterial( DeformableTerrainMaterial oldMat, DeformableTerrainMaterial newMat )
    {
      if ( Native == null )
        return true;

      if ( oldMat == null || newMat == null )
        return false;

      return Native.exchangeTerrainMaterial( oldMat.Native, newMat.Native );
    }

    public override void SetAssociatedMaterial( DeformableTerrainMaterial terrMat, ShapeMaterial shapeMat )
    {
      if ( Native == null )
        return;

      Native.setAssociatedMaterial( terrMat.Native, shapeMat.Native );
    }

    public override void AddTerrainMaterial( DeformableTerrainMaterial terrMat, Shape shape = null )
    {
      if ( Native == null )
        return;

      if ( shape == null )
        Native.addTerrainMaterial( terrMat.Native );
      else
        Native.addTerrainMaterial( terrMat.Native, shape.NativeGeometry );
    }

    protected override bool IsNativeNull() { return Native == null; }
    protected override void SetShapeMaterial( agx.Material material, agxTerrain.Terrain.MaterialType type ) { Native.setMaterial( material, type ); }
    protected override void SetTerrainMaterial( agxTerrain.TerrainMaterial material ) { Native.setTerrainMaterial( material ); }
    protected override void SetEnable( bool enable )
    {
      if ( Native == null )
        return;

      if ( Native.getEnable() == enable )
        return;

      Native.setEnable( enable );
      Native.getGeometry().setEnable( enable );
    }
  }
}
