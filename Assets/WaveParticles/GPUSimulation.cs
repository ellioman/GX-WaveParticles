using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System;

public class GPUSimulation : MonoBehaviour
{
    ComputeShader shaderWaveParticles;
    int kernelClear;
    int kernelReset;
    int kernelCollect;
    int kernelGenerate;
    int kernelOutput;

    RenderTexture rtOutput;
    ComputeBuffer bufferState;
    ComputeBuffer bufferCounter;
    ComputeBuffer bufferFreeList;

    static int textureSize = 256;
    static int bufferSize = 1024;

    bool hit = false;
    bool reset = false;
    bool updating = false;
    bool replay = false;

    public float particleRadius;
    public float waveRadius;
    public Vector2 planeSize;
    float currentTime;

    struct GenEvent
    {
        public float time;
        public Vector2 position;
    }
    List<GenEvent> events = new List<GenEvent>();

    int debugCounter = 0;
    WaveParticle[] debugParticles = new WaveParticle[bufferSize];

    [StructLayout(LayoutKind.Sequential)]
    [System.Diagnostics.DebuggerDisplay("{flags}")]
    struct WaveParticle
    {
        public float x, y;
        public float dx, dy;
        public float amplitude;
        public float dispersionAngle;
        public float birthTime;
        public uint flags;

        public Vector2 GetPosition(float currentTime)
        {
            float delta = currentTime - birthTime;
            return new Vector2(x, y) + new Vector2(dx, dy) * delta;
        }
    }

    void OnEnable()
    {
        shaderWaveParticles = (ComputeShader)Resources.Load("WaveParticles", typeof(ComputeShader));
        kernelClear = shaderWaveParticles.FindKernel("WaveParticlesClear");
        kernelReset = shaderWaveParticles.FindKernel("WaveParticlesReset");
        kernelCollect = shaderWaveParticles.FindKernel("WaveParticlesCollect");
        kernelGenerate = shaderWaveParticles.FindKernel("WaveParticlesGenerate");
        kernelOutput = shaderWaveParticles.FindKernel("WaveParticlesOutput");

        rtOutput = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
        rtOutput.enableRandomWrite = true;
        rtOutput.Create();

        bufferState = new ComputeBuffer(bufferSize, Marshal.SizeOf(typeof(WaveParticle)));
        bufferCounter = new ComputeBuffer(1, 4, ComputeBufferType.Raw);
        bufferFreeList = new ComputeBuffer(bufferSize, 4, ComputeBufferType.Append);

        reset = true;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            updating = !updating;
        }
        if (Input.GetKeyDown(KeyCode.D))
        {
            bufferState.GetData(debugParticles);
        }

        Step();

        if (Input.GetMouseButtonUp(0) && !hit)
        {
            Wave();
        }

        if (updating && replay)
        {
            int index = 0;
            for (; index < events.Count; index++)
            {
                var ev = events[index];
                if (currentTime > ev.time)
                {
                    Generate(ev.time, ev.position);
                }
                else
                {
                    break;
                }
            }

            events.RemoveRange(0, index);

            if (events.Count == 0)
            {
                replay = false;
            }
        }
    }

    void FixedUpdate()
    {
        if (updating)
        {
            currentTime += Time.fixedDeltaTime;
        }
    }

    [Flags]
    enum SetType
    {
        None = 0x0,
        FreeList = 0x1,
        Append = 0x2,
    }

    void SetBuffers(int kernel, SetType type)
    {
        shaderWaveParticles.SetInt("bufferSize", bufferSize);
        shaderWaveParticles.SetBuffer(kernel, "bufferState", bufferState);
        if ((type & SetType.FreeList) != SetType.None)
        {
            if ((type & SetType.Append) != SetType.None)
            {
                shaderWaveParticles.SetBuffer(kernel, "bufferFreeListAppend", bufferFreeList);
            }
            else
            {
                shaderWaveParticles.SetBuffer(kernel, "bufferFreeListConsume", bufferFreeList);
            }
        }
    }

    void Generate(float time, Vector2 position)
    {
        shaderWaveParticles.SetFloat("genTime", time);
        shaderWaveParticles.SetFloat("genWaveRadius", waveRadius);
        shaderWaveParticles.SetFloat("genParticleRadius", particleRadius);
        shaderWaveParticles.SetFloats("genCenter", position.x, position.y);
        SetBuffers(kernelGenerate, SetType.FreeList);
        shaderWaveParticles.Dispatch(kernelGenerate, 1, 1, 1);
        if (!replay)
        {
            events.Add(new GenEvent { time = time, position = position });
        }
    }

    const string EventsFileName = "events.bin";

    void SaveEvents()
    {
        using (var s = new BinaryWriter(new FileStream(EventsFileName, FileMode.Create)))
        {
            s.Write(events.Count);
            foreach (var ev in events)
            {
                s.Write(ev.time);
                s.Write(ev.position.x);
                s.Write(ev.position.y);
            }
        }

        Debug.LogFormat("Saved {0} events", events.Count);
    }

    void LoadEvents()
    {
        events.Clear();
        using (var s = new BinaryReader(new FileStream(EventsFileName, FileMode.Open)))
        {
            int count = s.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var ev = new GenEvent();
                ev.time = s.ReadSingle();
                ev.position.x = s.ReadSingle();
                ev.position.y = s.ReadSingle();
                events.Add(ev);
            }
        }
        for (int i = events.Count-1; i >= 0; i--)
        {
            var ev = events[i];
            //ev.time -= ;
            events[i] = ev;
        }

        Debug.LogFormat("Loaded {0} events", events.Count);
    }

    void Step()
    {
        if (reset)
        {
            currentTime = 0;
            if (replay)
            {
                currentTime = events[0].time - 1;
            }
            SetBuffers(kernelReset, SetType.None);
            shaderWaveParticles.Dispatch(kernelReset, bufferSize / 8, 1, 1);
            Debug.Log("Reset");
            reset = false;
        }

        bufferFreeList.SetCounterValue(0);
        SetBuffers(kernelCollect, SetType.FreeList | SetType.Append);
        shaderWaveParticles.Dispatch(kernelCollect, bufferSize / 8, 1, 1);

        var outOrigin = transform.position - transform.right * planeSize.x * 0.5f - transform.forward * planeSize.y * 0.5f;
        var outBasisX = transform.right;
        var outBasisY = transform.forward;

        // Clear
        shaderWaveParticles.SetTexture(kernelClear, "outResult", rtOutput);
        shaderWaveParticles.Dispatch(kernelClear, textureSize / 8, textureSize / 8, 1);

        // Render particles
        SetBuffers(kernelOutput, SetType.FreeList);
        shaderWaveParticles.SetTexture(kernelOutput, "outResult", rtOutput);
        shaderWaveParticles.SetFloat("outTime", currentTime);
        shaderWaveParticles.SetFloat("outParticleRadius", particleRadius);
        shaderWaveParticles.SetFloats("outOrigin", outOrigin.x, outOrigin.y, outOrigin.z);
        shaderWaveParticles.SetFloats("outBasisX", outBasisX.x, outBasisX.y, outBasisX.z);
        shaderWaveParticles.SetFloats("outBasisY", outBasisY.x, outBasisY.y, outBasisY.z);
        shaderWaveParticles.SetFloats("outPlaneSize", planeSize.x, planeSize.y);
        shaderWaveParticles.SetInt("outTextureSize", textureSize);
        shaderWaveParticles.Dispatch(kernelOutput, bufferSize / 8, 1, 1);

        int[] counter = new int[1];
        ComputeBuffer.CopyCount(bufferFreeList, bufferCounter, 0);
        bufferCounter.GetData(counter);
        if (counter[0] != debugCounter)
        {
            Debug.LogFormat("counter = {0}", counter[0]);
        }
        debugCounter = counter[0];
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

        Generate(currentTime, new Vector2(position.x, position.z));
    }

    void OnGUI()
    {
        var mousePos = Event.current.mousePosition;
        var hit = false;

        GUI.DrawTexture(new Rect(0, 0, rtOutput.width, rtOutput.height), rtOutput);
        if (GUILayout.Button("Clear"))
        {
            reset = true;
        }
        hit |= GUILayoutUtility.GetLastRect().Contains(mousePos);
        updating = GUILayout.Toggle(updating, "Updating");
        hit |= GUILayoutUtility.GetLastRect().Contains(mousePos);
        GUILayout.Label(string.Format("counter = {0}", debugCounter));

        if (GUILayout.Button("Clear events"))
        {
            events.Clear();
            replay = false;
        }
        hit |= GUILayoutUtility.GetLastRect().Contains(mousePos);
        if (GUILayout.Button("Save"))
        {
            SaveEvents();
        }
        hit |= GUILayoutUtility.GetLastRect().Contains(mousePos);
        if (GUILayout.Button("Load"))
        {
            LoadEvents();
        }
        hit |= GUILayoutUtility.GetLastRect().Contains(mousePos);
        GUILayout.Label(string.Format("ev = {0}", events.Count));
        if (events.Count > 0 && GUILayout.Button(replay ? "Replaying" : "Replay"))
        {
            reset = true;
            replay = true;
        }
        hit |= GUILayoutUtility.GetLastRect().Contains(mousePos);
        if (Event.current.type == EventType.Repaint) this.hit = hit;
    }

    Vector2 Project(Vector3 worldPosition)
    {
        var outOrigin = transform.position - transform.right * planeSize.x * 0.5f - transform.forward * planeSize.y * 0.5f;
        var outBasisX = transform.right;
        var outBasisY = transform.forward;
        var direction = worldPosition - outOrigin;
        var proj = new Vector2(Vector3.Dot(direction, outBasisX), Vector3.Dot(direction, outBasisY));
        Debug.LogFormat("{0} {1} {2} {3} {4}", outOrigin, outBasisX, outBasisY, direction, proj);
        return Vector2.Scale(proj, new Vector2(1.0f / planeSize.x, 1.0f / planeSize.y));
    }

    void OnDrawGizmos()
    {
        var halfPlaneSizeX = transform.right * planeSize.x * 0.5f;
        var halfPlaneSizeY = transform.forward * planeSize.y * 0.5f;
        Debug.DrawLine(transform.position - halfPlaneSizeX - halfPlaneSizeY, transform.position + halfPlaneSizeX - halfPlaneSizeY, Color.green);
        Debug.DrawLine(transform.position + halfPlaneSizeX - halfPlaneSizeY, transform.position + halfPlaneSizeX + halfPlaneSizeY, Color.green);
        Debug.DrawLine(transform.position + halfPlaneSizeX + halfPlaneSizeY, transform.position - halfPlaneSizeX + halfPlaneSizeY, Color.green);
        Debug.DrawLine(transform.position - halfPlaneSizeX + halfPlaneSizeY, transform.position - halfPlaneSizeX - halfPlaneSizeY, Color.green);

        foreach (var particle in debugParticles)
        {
            if (particle.flags != 0)
            {
                DebugExtension.DebugCircle(new Vector3(particle.x, 0, particle.y), Color.magenta, particleRadius);
                DebugExtension.DrawArrow(new Vector3(particle.x, 0, particle.y), new Vector3(particle.dx, 0, particle.dy), Color.magenta);
                var currentPosition = particle.GetPosition(currentTime);
                DebugExtension.DebugCircle(new Vector3(currentPosition.x, 0, currentPosition.y), Color.blue, particleRadius);

                var proj = Project(new Vector3(currentPosition.x, 0, currentPosition.y));
                Debug.LogFormat("{0} {1}", proj.x * textureSize, proj.y * textureSize);

                break;
            }
        }
    }
}
