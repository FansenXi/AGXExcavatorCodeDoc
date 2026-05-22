using AGXUnity_Excavator.Scripts.SimulationBridge;
using UnityEngine;
using UnityEngine.Rendering;

namespace AGXUnity_Excavator.Scripts.Presentation
{
  public class PlannerDecisionVisualizer : MonoBehaviour
  {
    [SerializeField]
    private AgxSimStepAckServer m_stepAckServer = null;

    [SerializeField]
    private global::DigAreaMeasurement m_digAreaMeasurement = null;

    [SerializeField]
    [Min( 0.0f )]
    private float m_pointerTopHeightMeters = 0.65f;

    [SerializeField]
    [Min( 0.0f )]
    private float m_pointerTipHeightMeters = 0.035f;

    [SerializeField]
    [Min( 0.001f )]
    private float m_selectedWidth = 0.025f;

    [SerializeField]
    [Min( 0.001f )]
    private float m_depletedWidth = 0.018f;

    [SerializeField]
    private Color m_selectedColor = new Color( 0.05f, 0.95f, 1.0f, 0.95f );

    [SerializeField]
    private Color m_entryMarkerColor = new Color( 0.25f, 1.0f, 0.35f, 0.95f );

    [SerializeField]
    private Color m_exitMarkerColor = new Color( 1.0f, 0.85f, 0.2f, 0.65f );

    [SerializeField]
    private Color m_depletedColor = new Color( 1.0f, 0.4f, 0.2f, 0.35f );

    private LineRenderer m_selectedLine = null;
    private LineRenderer m_entryMarker = null;
    private LineRenderer m_exitMarker = null;
    private LineRenderer[] m_depletedLines = new LineRenderer[ 0 ];
    private Material m_lineMaterial = null;

    public void Configure( AgxSimStepAckServer stepAckServer )
    {
      m_stepAckServer = stepAckServer != null ? stepAckServer : m_stepAckServer;
      ResolveReferences();
    }

    private void OnEnable()
    {
      ResolveReferences();
      EnsureRenderers();
    }

    private void LateUpdate()
    {
      ResolveReferences();
      EnsureRenderers();
      RefreshVisuals();
    }

    private void OnDisable()
    {
      HideAll();
    }

    private void OnDestroy()
    {
      if ( m_lineMaterial != null )
        Destroy( m_lineMaterial );
      m_lineMaterial = null;
    }

    private void ResolveReferences()
    {
      if ( m_stepAckServer == null ) {
        var servers = Object.FindObjectsByType<AgxSimStepAckServer>(
          FindObjectsInactive.Include,
          FindObjectsSortMode.None );
        if ( servers != null && servers.Length > 0 )
          m_stepAckServer = servers[ 0 ];
      }
      if ( m_digAreaMeasurement == null )
        m_digAreaMeasurement = global::DigAreaMeasurement.FindOrCreateInScene();
    }

    private void RefreshVisuals()
    {
      var snapshot = m_stepAckServer != null ?
                     m_stepAckServer.LastPlannerDebug :
                     PlannerDebugSnapshot.Empty();
      if ( snapshot == null || !snapshot.HasCoverageDecision || m_digAreaMeasurement == null ) {
        HideAll();
        return;
      }

      if ( !TryEntryPointerWorldPoints( snapshot.entry_x_m,
                                        snapshot.entry_z_m,
                                        out var pointerTopWorld,
                                        out var pointerTipWorld ) ) {
        HideAll();
        return;
      }

      SetLine( m_selectedLine, pointerTopWorld, pointerTipWorld, m_selectedColor, m_selectedWidth, true );
      SetMarker( m_entryMarker, pointerTipWorld, m_entryMarkerColor, true );
      if ( m_exitMarker != null )
        m_exitMarker.enabled = false;
      RefreshDepletedCorridors( snapshot );
    }

    private void RefreshDepletedCorridors( PlannerDebugSnapshot snapshot )
    {
      var corridors = snapshot.corridors ?? new PlannerDebugCorridor[ 0 ];
      EnsureDepletedLineCount( corridors.Length );
      var depletedLineIndex = 0;
      for ( var index = 0; index < corridors.Length; ++index ) {
        var corridor = corridors[ index ];
        if ( corridor == null || corridor.depleted == 0 || corridor.corridor_id == snapshot.selected_corridor_id )
          continue;
        if ( depletedLineIndex >= m_depletedLines.Length )
          break;
        if ( TryEntryPointerWorldPoints( corridor.entry_x_m,
                                         corridor.entry_z_m,
                                         out var pointerTopWorld,
                                         out var pointerTipWorld ) ) {
          SetLine( m_depletedLines[ depletedLineIndex ],
                   pointerTopWorld,
                   pointerTipWorld,
                   m_depletedColor,
                   m_depletedWidth,
                   true );
          depletedLineIndex++;
        }
      }

      for ( var index = depletedLineIndex; index < m_depletedLines.Length; ++index ) {
        if ( m_depletedLines[ index ] != null )
          m_depletedLines[ index ].enabled = false;
      }
    }

    private bool TryEntryPointerWorldPoints( float entryX,
                                             float entryZ,
                                             out Vector3 pointerTopWorld,
                                             out Vector3 pointerTipWorld )
    {
      pointerTopWorld = Vector3.zero;
      pointerTipWorld = Vector3.zero;
      return m_digAreaMeasurement != null &&
             m_digAreaMeasurement.TryDigAreaLocalPlanePointWorld(
               entryX,
               entryZ,
               m_pointerTopHeightMeters,
               out pointerTopWorld ) &&
             m_digAreaMeasurement.TryDigAreaLocalPlanePointWorld(
               entryX,
               entryZ,
               m_pointerTipHeightMeters,
               out pointerTipWorld );
    }

    private void EnsureRenderers()
    {
      EnsureMaterial();
      m_selectedLine = GetOrCreateLineRenderer( "PlannerSelectedCorridor", m_selectedLine, 40 );
      m_entryMarker = GetOrCreateLineRenderer( "PlannerEntryMarker", m_entryMarker, 41 );
      m_exitMarker = GetOrCreateLineRenderer( "PlannerExitMarker", m_exitMarker, 42 );
    }

    private void EnsureDepletedLineCount( int count )
    {
      count = Mathf.Clamp( count, 0, 9 );
      if ( m_depletedLines != null && m_depletedLines.Length >= count )
        return;

      var next = new LineRenderer[ count ];
      if ( m_depletedLines != null ) {
        for ( var index = 0; index < Mathf.Min( m_depletedLines.Length, next.Length ); ++index )
          next[ index ] = m_depletedLines[ index ];
      }
      m_depletedLines = next;
      for ( var index = 0; index < m_depletedLines.Length; ++index )
        m_depletedLines[ index ] = GetOrCreateLineRenderer(
          $"PlannerDepletedCorridor{index}",
          m_depletedLines[ index ],
          30 + index );
    }

    private LineRenderer GetOrCreateLineRenderer( string childName,
                                                  LineRenderer existing,
                                                  int sortingOrder )
    {
      if ( existing != null )
        return existing;

      var child = transform.Find( childName );
      if ( child == null ) {
        var childObject = new GameObject( childName )
        {
          hideFlags = HideFlags.DontSave
        };
        childObject.transform.SetParent( transform, false );
        child = childObject.transform;
      }

      var renderer = child.GetComponent<LineRenderer>();
      if ( renderer == null )
        renderer = child.gameObject.AddComponent<LineRenderer>();
      renderer.sharedMaterial = m_lineMaterial;
      renderer.loop = false;
      renderer.useWorldSpace = true;
      renderer.positionCount = 2;
      renderer.textureMode = LineTextureMode.Stretch;
      renderer.numCornerVertices = 2;
      renderer.numCapVertices = 2;
      renderer.shadowCastingMode = ShadowCastingMode.Off;
      renderer.receiveShadows = false;
      renderer.allowOcclusionWhenDynamic = false;
      renderer.sortingOrder = sortingOrder;
      renderer.enabled = false;
      return renderer;
    }

    private void EnsureMaterial()
    {
      if ( m_lineMaterial != null )
        return;

      var shader = Shader.Find( "Hidden/Internal-Colored" ) ??
                   Shader.Find( "Sprites/Default" ) ??
                   Shader.Find( "Legacy Shaders/Particles/Alpha Blended Premultiply" ) ??
                   Shader.Find( "Unlit/Color" );
      if ( shader == null )
        return;

      m_lineMaterial = new Material( shader )
      {
        name = "PlannerDecisionRuntimeMaterial",
        hideFlags = HideFlags.DontSave
      };
      if ( m_lineMaterial.HasProperty( "_Color" ) )
        m_lineMaterial.color = Color.white;
      if ( m_lineMaterial.HasProperty( "_SrcBlend" ) )
        m_lineMaterial.SetInt( "_SrcBlend", (int)BlendMode.SrcAlpha );
      if ( m_lineMaterial.HasProperty( "_DstBlend" ) )
        m_lineMaterial.SetInt( "_DstBlend", (int)BlendMode.OneMinusSrcAlpha );
      if ( m_lineMaterial.HasProperty( "_Cull" ) )
        m_lineMaterial.SetInt( "_Cull", (int)CullMode.Off );
      if ( m_lineMaterial.HasProperty( "_ZWrite" ) )
        m_lineMaterial.SetInt( "_ZWrite", 0 );
      if ( m_lineMaterial.HasProperty( "_ZTest" ) )
        m_lineMaterial.SetInt( "_ZTest", (int)CompareFunction.Always );
      m_lineMaterial.renderQueue = 3125;
    }

    private static void SetLine( LineRenderer renderer,
                                 Vector3 start,
                                 Vector3 end,
                                 Color color,
                                 float width,
                                 bool enabled )
    {
      if ( renderer == null )
        return;
      renderer.positionCount = 2;
      renderer.SetPosition( 0, start );
      renderer.SetPosition( 1, end );
      renderer.startWidth = width;
      renderer.endWidth = width;
      renderer.startColor = color;
      renderer.endColor = color;
      renderer.enabled = enabled;
    }

    private void SetMarker( LineRenderer renderer,
                            Vector3 center,
                            Color color,
                            bool enabled )
    {
      var radius = Mathf.Max( 0.03f, m_selectedWidth * 1.1f );
      SetLine(
        renderer,
        center - Vector3.right * radius,
        center + Vector3.right * radius,
        color,
        Mathf.Max( 0.02f, m_selectedWidth * 0.65f ),
        enabled );
    }

    private void HideAll()
    {
      if ( m_selectedLine != null )
        m_selectedLine.enabled = false;
      if ( m_entryMarker != null )
        m_entryMarker.enabled = false;
      if ( m_exitMarker != null )
        m_exitMarker.enabled = false;
      if ( m_depletedLines == null )
        return;
      for ( var index = 0; index < m_depletedLines.Length; ++index ) {
        if ( m_depletedLines[ index ] != null )
          m_depletedLines[ index ].enabled = false;
      }
    }
  }
}
