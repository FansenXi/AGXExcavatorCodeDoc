using agxROS2;
using AGXUnity;
using AGXUnity_Excavator.Scripts.GraphPerception;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Ros2Bridge.Publishers
{
  [AddComponentMenu( "AGXUnity Excavator/ROS2/Terrain Graph JSON Publisher" )]
  [RequireComponent( typeof( TerrainGraphObservationProvider ) )]
  public class TerrainGraphJsonRos2Publisher : ScriptComponent
  {
    [SerializeField]
    [Tooltip( "ROS 2 topic used for the transitional std_msgs/String JSON terrain graph stream." )]
    private string m_topic = "terrain_graph/json";

    [SerializeField]
    [Tooltip( "Publish every N AGX simulation steps. Use 1 for every step." )]
    [Min( 1 )]
    private int m_publishEverySteps = 1;

    [SerializeField]
    [Tooltip( "When true, the provider output is pretty-printed. Keep false for lower bandwidth." )]
    private bool m_prettyPrint = false;

    [SerializeField]
    [Tooltip( "QOS settings for the JSON terrain graph topic." )]
    private AGXUnity.Sensor.QOS m_qos = new AGXUnity.Sensor.QOS();

    private TerrainGraphObservationProvider m_provider;
    private PublisherStdMsgsString m_publisher;
    private int m_stepCounter;

    public string Topic => m_topic;

    protected override bool Initialize()
    {
      m_provider = GetComponent<TerrainGraphObservationProvider>();
      if ( m_provider == null ) {
        Debug.LogError( "TerrainGraphJsonRos2Publisher requires a TerrainGraphObservationProvider.", this );
        return false;
      }

      return true;
    }

    protected override void OnEnable()
    {
      base.OnEnable();

      if ( Simulation.HasInstance )
        Simulation.Instance.StepCallbacks.PostStepForward += PublishIfDue;
    }

    protected override void OnDisable()
    {
      if ( Simulation.HasInstance )
        Simulation.Instance.StepCallbacks.PostStepForward -= PublishIfDue;

      base.OnDisable();
    }

    protected override void OnDestroy()
    {
      m_publisher = null;
      m_provider = null;
      base.OnDestroy();
    }

    private void PublishIfDue()
    {
      if ( m_provider == null )
        m_provider = GetComponent<TerrainGraphObservationProvider>();

      if ( m_provider == null )
        return;

      m_stepCounter++;
      if ( m_stepCounter % Mathf.Max( 1, m_publishEverySteps ) != 0 )
        return;

      var observation = m_provider.Collect();
      var json = JsonUtility.ToJson( observation, m_prettyPrint );

      if ( m_publisher == null )
        m_publisher = new PublisherStdMsgsString( m_topic, m_qos.CreateNative() );

      var message = new StdMsgsString {
        data = json
      };
      m_publisher.sendMessage( message );
    }
  }
}
