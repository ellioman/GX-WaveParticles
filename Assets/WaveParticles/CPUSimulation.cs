using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class CPUSimulation : MonoBehaviour
{
	public class WaveParticle
	{
		public Vector2 birthPosition;
		public Vector2 direction;
		public float amplitude;
		public float dispersionAngle;
		public float birthTime;

		public Vector2 GetPosition(float currentTime)
		{
			float delta = currentTime - birthTime;
			return birthPosition + direction * delta;
		}

		public Vector2 GetRotatedPosition(float currentTime, float angle)
		{
			float delta = currentTime - birthTime;
			return birthPosition + Utils.Rotate(direction, angle) * delta;
		}
	}

	public struct WaveEvent
	{
		public enum Type
		{
			Subdivision
		}

		public readonly Type type;
		public readonly int particle;

		private WaveEvent(Type type, int particle)
		{
			this.type = type;
			this.particle = particle;
		}

		public static WaveEvent NewSubdivision(int particle)
		{
			return new WaveEvent(Type.Subdivision, particle);
		}
	}

	public float particleRadius;
	public float waveRadius;
	public Vector2 planeSize;

	bool updating = false;
	bool hit = false;
	bool stopOnSubdivision = false;
	float currentTime;
	bool dragging = false;
	List<WaveParticle> particles = new List<WaveParticle>();
	SortedDictionary<float, List<WaveEvent>> events = new SortedDictionary<float, List<WaveEvent>>();

	void QueueSubdivision(float time, int particle)
	{
		List<WaveEvent> list;
		if (!events.TryGetValue(time, out list))
		{
			list = new List<WaveEvent>();
			events.Add(time, list);
		}
		list.Add(WaveEvent.NewSubdivision(particle));
	}

	void CreateParticle(Vector3 position, Vector2 direction, float amplitude, float dispersionAngle)
	{
		int index = particles.Count;
		particles.Add(new WaveParticle
		{
			birthPosition = new Vector2(position.x, position.z),
			direction = direction,
			amplitude = amplitude,
			dispersionAngle = dispersionAngle,
			birthTime = currentTime,
		});
		float subdivisionTime = 0.0f;
		QueueSubdivision(subdivisionTime, index);
	}

	void SubdivideParticle(WaveParticle particle)
	{
        var sc = new Vector2(Mathf.Sin(particle.dispersionAngle / 2.0f), Mathf.Cos(particle.dispersionAngle / 2.0f));

        particles.Add(new WaveParticle
		{
			birthPosition = particle.birthPosition,
			direction = Utils.RotateCW(particle.direction, sc),
			amplitude = particle.amplitude / 3.0f,
			dispersionAngle = particle.dispersionAngle / 3.0f,
			birthTime = particle.birthTime,
		});

		particles.Add(new WaveParticle
		{
			birthPosition = particle.birthPosition,
			direction = Utils.RotateCCW(particle.direction, sc),
			amplitude = particle.amplitude / 3.0f,
			dispersionAngle = particle.dispersionAngle / 3.0f,
			birthTime = particle.birthTime,
		});

		particle.dispersionAngle /= 3.0f;
		particle.amplitude /= 3.0f;
	}

	bool RaycastParticle(out Vector3 position)
	{
		var plane = new Plane(Vector3.up, 0);
		var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
		Debug.LogFormat("Mouse at {0}", Input.mousePosition);
		float distance;
		if (plane.Raycast(ray, out distance))
		{
			position = ray.GetPoint(distance);
			return true;
		}
		position = Vector3.zero;
		return false;
	}

	void Wave()
	{
		Vector3 position;
		if (!RaycastParticle(out position))
		{
			return;
		}

		var v0 = new Vector2(waveRadius, 0);
		var len = Mathf.Sqrt(waveRadius * waveRadius - particleRadius * particleRadius);
		var h = particleRadius * len / waveRadius;
		var v1 = new Vector2(Mathf.Sqrt(len * len - h * h), h);
		var angle = Mathf.Deg2Rad * Vector2.Angle(v0, v1) * 2.0f;

		int n = Mathf.FloorToInt(Mathf.PI * 2.0f / angle);
		var newAngle = Mathf.PI * 2.0f / n;
		Debug.LogFormat("Wave v0x={0} v0y={1} len={2} h={3} v1x={4} v1y={5} angle={6} n={7}", v0.x, v0.y, len, h, v1.x, v1.y, angle, n);
		for (int i = 0; i < n; i++)
		{
			float currentAngle = i * newAngle;
			//var currentPosition = new Vector3(position.x + Mathf.Cos(currentAngle) * waveRadius, 0, position.z + Mathf.Sin(currentAngle) * waveRadius);
			var currentPosition = position;
			CreateParticle(currentPosition, new Vector2(Mathf.Cos(currentAngle), Mathf.Sin(currentAngle)), 1, newAngle);
			//break;
		}
	}

	void Update()
	{
		if (Input.GetMouseButtonDown(0) && !hit)
		{
			dragging = true;
		}
		if (Input.GetMouseButtonUp(0))
		{
			dragging = false;
		}
		if (Input.GetKeyDown(KeyCode.Space))
		{
			updating = !updating;
		}
	}

	void FixedUpdate()
	{
		if (dragging)
		{
			Wave();
			dragging = false;
		}

		if (updating)
		{
			currentTime += Time.fixedDeltaTime;
		}

		var particlesToSubdivide = new List<WaveParticle>();

		float minAmplitude = 1.0f;

		foreach (var particle in particles)
		{
			var currentPosition = particle.GetPosition(currentTime);
            //var v0 = particle.direction * (currentTime - particle.birthTime);
            //var v1 = Utils.Rotate(v0, particle.dispersionAngle);
            //var d = Vector2.Distance(v0, v1);
            float deltaTime = currentTime - particle.birthTime;
            var d = deltaTime * Mathf.Sin(particle.dispersionAngle / 4.0f);
            //var d = Utils.ChordLength(deltaTime, particle.dispersionAngle / 2.0f) * 2.0f;
            if (minAmplitude > particle.amplitude)
            {
                minAmplitude = particle.amplitude;
            }
            if (updating && d > particleRadius)
            {
                particlesToSubdivide.Add(particle);
            }
            //var d = currentTime - particle.birthTime;
            //var a0 = particle.dispersionAngle * d;
        }

		if (updating && minAmplitude < 1.0f)
		{
			Debug.Log(minAmplitude);
		}

		if (stopOnSubdivision && particlesToSubdivide.Count > 0)
		{
            Debug.Log(particles.Count);
			updating = false;
		}

		foreach (var particle in particlesToSubdivide)
		{
			SubdivideParticle(particle);
		}
	}

	void OnDrawGizmos()
	{
		var halfPlaneSize = planeSize * 0.5f;
		Debug.DrawLine(new Vector3(-halfPlaneSize.x, 0, -halfPlaneSize.y), new Vector3(+halfPlaneSize.x, 0, -halfPlaneSize.y), Color.green);
		Debug.DrawLine(new Vector3(+halfPlaneSize.x, 0, -halfPlaneSize.y), new Vector3(+halfPlaneSize.x, 0, +halfPlaneSize.y), Color.green);
		Debug.DrawLine(new Vector3(+halfPlaneSize.x, 0, +halfPlaneSize.y), new Vector3(-halfPlaneSize.x, 0, +halfPlaneSize.y), Color.green);
		Debug.DrawLine(new Vector3(-halfPlaneSize.x, 0, +halfPlaneSize.y), new Vector3(-halfPlaneSize.x, 0, -halfPlaneSize.y), Color.green);
		foreach (var particle in particles)
		{
			DebugExtension.DebugCircle(new Vector3(particle.birthPosition.x, 0, particle.birthPosition.y), Color.magenta, particleRadius);
			DebugExtension.DrawArrow(new Vector3(particle.birthPosition.x, 0, particle.birthPosition.y), new Vector3(particle.direction.x, 0, particle.direction.y), Color.magenta);
			var currentPosition = particle.GetPosition(currentTime);
			DebugExtension.DebugCircle(new Vector3(currentPosition.x, 0, currentPosition.y), Color.blue, particleRadius);
		}
	}

	void OnGUI()
	{
		var mousePos = Event.current.mousePosition;
		var hit = false;
		if (GUILayout.Button("Clear"))
		{
			particles.Clear();
		}
		hit |= GUILayoutUtility.GetLastRect().Contains(mousePos);
		updating = GUILayout.Toggle(updating, "Updating");
		hit |= GUILayoutUtility.GetLastRect().Contains(mousePos);
		stopOnSubdivision = GUILayout.Toggle(stopOnSubdivision, "Stop on subdiv");
		hit |= GUILayoutUtility.GetLastRect().Contains(mousePos);
		if (Event.current.type == EventType.Repaint) this.hit = hit;
	}
}
