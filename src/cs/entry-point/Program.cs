using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using JackCS;
using Microsoft.Win32.SafeHandles;

var appsettings = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var ladspaSettings = appsettings.GetSection("Ladspa").Get<LadspaSettings>();

Console.WriteLine($"Discovering LADSPA plugins in directory {ladspaSettings.PluginDirectory} as {ladspaSettings.PluginFileExtension} ...");
int pluginCount = 0;
var loader = new LadspaLoader();

Dictionary<nuint, IntPtr> LADSPAPlugins = new Dictionary<nuint, nint>();
LadspaDescriptor? sc1 = null;


foreach (var file in Directory.GetFiles(ladspaSettings.PluginDirectory, $"*{ladspaSettings.PluginFileExtension}"))
{
    Console.WriteLine($"\t{file}");
    ++pluginCount;

    IntPtr ptr = IntPtr.Zero;

    var descriptor = loader.LoadDescriptor(file, out ptr);

    if (descriptor.HasValue)
    {


        var d = descriptor.Value;
        string name = GetSafeString(d.Name);
        string maker = GetSafeString(d.Maker);

        Console.WriteLine($"\t\tFound Plugin: {name}");
        Console.WriteLine($"\t\tAuthor: {maker}");
        Console.WriteLine($"\t\tID: {d.UniqueID}");
        Console.WriteLine($"\t\tNumber of Ports: {d.PortCount}");

        if (d.UniqueID == 1425)
        {
            sc1 = d; // put into a dict for heaven's sake!!
            LADSPAPlugins[d.UniqueID] = ptr;
        }


        // Use nuint for the loop counter to match d.PortCount
        for (nuint port = 0; port < d.PortCount; port++)
        {
            // 1. Read PortDescriptor (int / 32-bit)
            // Offset is: port index * 4 bytes
            int portDescValue = Marshal.ReadInt32(d.PortDescriptors, (int)port * sizeof(int));
            var pd = (PortDescriptor)portDescValue;

            // 2. Read PortName (pointer / 8-byte)
            // Offset is: port index * IntPtr.Size (8 bytes on 64-bit)
            IntPtr portNamePtr = Marshal.ReadIntPtr(d.PortNames, (int)port * IntPtr.Size);
            string portName = GetSafeString(portNamePtr);

            Console.Write($"\t\t\tPort {port} - {portName}: ");

            // 3. Evaluate Flags
            // It's safer to check bits directly or use HasFlag
            bool isAudio = (pd & PortDescriptor.Audio) != 0;
            bool isControl = (pd & PortDescriptor.Control) != 0;
            bool isInput = (pd & PortDescriptor.Input) != 0;
            bool isOutput = (pd & PortDescriptor.Output) != 0;

            if (isAudio)
            {
                Console.WriteLine($"Audio ({(isInput ? "Input" : "Output")})");
            }
            else if (isControl)
            {
                Console.WriteLine($"Control ({(isInput ? "Input" : "Output")})");
            }
            else
            {
                Console.WriteLine("Unknown Type");
            }
        }
    }
}
Console.WriteLine($"... found {pluginCount} LADSPA plugins.");

string GetSafeString(IntPtr ptr)
{
    if (ptr == IntPtr.Zero) return "N/A";
    
    // We use a try-catch because if the pointer is truly 'evil', 
    // even manual reading will trigger the AccessViolation.
    try {
        return Marshal.PtrToStringAnsi(ptr) ?? "Unknown";
    }
    catch {
        return "Invalid Pointer";
    }
}

void InspectPorts(LadspaDescriptor d)
{
    for (nuint i = 0; i < d.PortCount; i++)
    {
        // 1. Get the descriptor bitmask (LADSPA_PortDescriptor is 'int' in C, so 4 bytes)
        // We cast 'i' to int for the offset, as Marshal.ReadInt32 expects an int offset.
        int mask = Marshal.ReadInt32(d.PortDescriptors, (int)i * sizeof(int));
        var type = (PortDescriptor)mask;

        // 2. Get the pointer to the name string (Pointer size is 8 bytes on 64-bit)
        IntPtr namePtr = Marshal.ReadIntPtr(d.PortNames, (int)i * IntPtr.Size);
        string portName = Marshal.PtrToStringAnsi(namePtr) ?? $"Port {i}";

        // 3. Categorize using bitwise checks (slightly more performant than HasFlag)
        bool isAudio = (type & PortDescriptor.Audio) != 0;
        bool isControl = (type & PortDescriptor.Control) != 0;
        bool isInput = (type & PortDescriptor.Input) != 0;

        if (isAudio)
        {
            string direction = isInput ? "In" : "Out";
            Console.WriteLine($"[AUDIO]  Index {i}: {portName} ({direction})");
        }
        else if (isControl)
        {
            string direction = isInput ? "Input (Knob)" : "Output (Meter)";
            Console.WriteLine($"[CONTROL] Index {i}: {portName} ({direction})");
        }
    }
}

// JACK -> LADSPA


if (null == sc1)
{
    Console.WriteLine("We didn't get SC1!!");
    return;
}

Console.WriteLine("JackToLadspa_SC1 ...");
var jackToLadspa_SC1 = new JackToLadspa_SC1();
var ptrSc1 = LADSPAPlugins[1425];

DebugDescriptor(ptrSc1);

Console.WriteLine("init ...");
jackToLadspa_SC1.InitializePlugin(sc1.Value, ptrSc1, 48000);
Console.WriteLine("... init done!!");

void DebugDescriptor(IntPtr ptr)
{
    Console.WriteLine($"--- Memory Scan of Descriptor at {ptr:X} ---");
    for (int i = 0; i < 15; i++)
    {
        IntPtr val = Marshal.ReadIntPtr(ptr, i * 8);
        Console.WriteLine($"Offset {i * 8}: {val:X}");
    }
}

// JACK --------------   see https://github.com/Beyley/LoudPizza/blob/main/LoudPizza.Backends.Jack2/JackBackend.cs
var jackTest = new JackTest();

jackTest.Test(jackToLadspa_SC1);   // we need something way better than this!!!

[DllImport("libjack.so.0", EntryPoint="jack_port_get_buffer")]
static extern unsafe void* JackPortGetBuffer(IntPtr port, uint nFrames);

class JackTest 
{
    public void Test(JackToLadspa_SC1 sc1) 
    {
        try 
        {

            unsafe
            {
                Jack    _jack = Jack.GetApi();

                Console.WriteLine(_jack);

                Client* _client = _jack.ClientOpen("MiStomp", JackOptions.JackNullOption, null);;

                var sampleRate = _jack.GetSampleRate(_client);

                Console.WriteLine(sampleRate);

                uint bufferSize = 512;

                _jack.SetBufferSize(_client, bufferSize);

                int i = 0;

                Console.WriteLine("Registering pre in/out port ...");
                Port* inPortPre = _jack.PortRegister(_client, $"guitar_in_pre", Jack.DefaultAudioType, (uint)JackPortFlags.JackPortIsInput, 0);
                Port* outPortPre = _jack.PortRegister(_client, $"guitar_out_pre", Jack.DefaultAudioType, (uint)JackPortFlags.JackPortIsOutput, 0);

                Console.WriteLine("Registering post in/out port ...");
                Port* inPortPost = _jack.PortRegister(_client, $"guitar_in_post", Jack.DefaultAudioType, (uint)JackPortFlags.JackPortIsInput, 0);
                Port* outPortPost = _jack.PortRegister(_client, $"guitar_out_post", Jack.DefaultAudioType, (uint)JackPortFlags.JackPortIsOutput, 0);

                Console.WriteLine("Ports registered");

                // 2. Automated Connection for Audio IN
                // We look for Physical Outputs (Capture devices/Mics)
                byte** physicalOutputs = _jack.GetPorts(_client, (byte*)null, Jack.DefaultAudioType, 
                    (uint)(JackPortFlags.JackPortIsPhysical | JackPortFlags.JackPortIsOutput));

                if (physicalOutputs != null)
                {
                    // Connect the first physical output (System Capture 1) to our Input Port
                    _jack.Connect(_client, physicalOutputs[0], _jack.PortName(inPortPre));

                    if (true && null != inPortPost) // TODO :: bounds check somehow!! physicalOutputs.Length >= 2)
                    {
                        _jack.Connect(_client, physicalOutputs[1], _jack.PortName(inPortPost));
                    }
                    else
                    {
                        Console.WriteLine("WARN!! could not connect post guitar in");
                    }

                    _jack.Free(physicalOutputs); // Clean up the list memory
                }

                // 3. Automated Connection for Audio OUT
                // We look for Physical Inputs (Playback devices/Speakers)
                byte** physicalInputs = _jack.GetPorts(_client, (byte*)null, Jack.DefaultAudioType, 
                    (uint)(JackPortFlags.JackPortIsPhysical | JackPortFlags.JackPortIsInput));

                if (physicalInputs != null)
                {
                    // Connect our Output Port to the first physical input (System Playback 1)
                    _jack.Connect(_client, _jack.PortName(outPortPre), physicalInputs[0]);

                    if (true && null != outPortPost) // TODO :: bounds check somehow!! //physicalOutputs.Length >= 2)
                    {
                        _jack.Connect(_client, _jack.PortName(outPortPost), physicalInputs[1]);
                    }
                    else
                    {
                        Console.WriteLine("WARN!! could not connect post guitar out");
                    }

                    _jack.Free(physicalInputs);
                }

                GCHandle _this;

                _this = GCHandle.Alloc(this, GCHandleType.Normal);

                Console.WriteLine("Setting Jack process callback ...");
                var setProcessCallback = _jack.SetProcessCallback(_client, new PfnJackProcessCallback(JackCallback), (void*)GCHandle.ToIntPtr(_this));

                Console.WriteLine($"setProcessCallback: {setProcessCallback}");
                if (setProcessCallback != 0)
                {
                    Console.WriteLine("ERROR!! didnae set process callback");    
                }

                Console.WriteLine("Activating Jack ...");
                var activate = _jack.Activate(_client);

                Console.WriteLine(activate);
                if (activate != 0)
                {
                    Console.WriteLine("ERROR!! didnae activate");
                }

                Console.Write("Press RETURN to exit ...");
                Console.Read();

                Console.WriteLine("Closing JACK");
                _jack.ClientClose(_client);

                int JackCallback(uint frames, void* usrData) 
                {
                    try 
                    {       
                        unsafe
                        {
                            const float ATTENUATION_GAIN = 0.01f;
                           
                            float* preInBuf = (float*)_jack.PortGetBuffer(inPortPre, frames);
                            float* preOutBuf = (float*)_jack.PortGetBuffer(outPortPre, frames);

                            float* postInBuf = (float*)_jack.PortGetBuffer(inPortPost, frames);
                            float* postOutBuf = (float*)_jack.PortGetBuffer(outPortPost, frames);

                            //fixed (float* preInBuf = (float*)_jack.PortGetBuffer(inPortPre, frames))
                            //fixed (float* preOutBuf = (float*)_jack.PortGetBuffer(outPortPre, frames))
                            //fixed (float* postInBuf = (float*)_jack.PortGetBuffer(inPortPost, frames))
                            //fixed (float* postOutBuf = (float*)_jack.PortGetBuffer(outPortPost, frames)) 
                            {
                                sc1._connectPort(sc1._pluginHandle, JackToLadspa_SC1.SC1_INPUT, (IntPtr)preInBuf);
                                sc1._connectPort(sc1._pluginHandle, JackToLadspa_SC1.SC1_OUTPUT, (IntPtr)preOutBuf);

                                sc1._runPlugin(sc1._pluginHandle, frames);

                                for (int i = 0; i < frames; i++)
                                {
                                    preOutBuf[i] = preInBuf[i] * ATTENUATION_GAIN;
                                    postOutBuf[i] = postInBuf[i] * ATTENUATION_GAIN;
                                }                         
                            }
                        }
                    }
                    catch (Exception handlerEx)
                    {
                        Console.WriteLine($"handler exception - {handlerEx.ToString()}");            
                    }

                    return 0;

                }
            }
        }
        catch (Exception outerEx)
        {
            Console.WriteLine($"outer exception - {outerEx.ToString()}");
        }
    }

}

[StructLayout(LayoutKind.Explicit, Size = 152)] // Increased size to accommodate shift
public struct LadspaDescriptor
{
    [FieldOffset(0)]  public nuint UniqueID;
    [FieldOffset(8)]  public IntPtr Label;
    [FieldOffset(16)] public int Properties;
    
    [FieldOffset(24)] public IntPtr Name;
    [FieldOffset(32)] public IntPtr Maker;
    [FieldOffset(40)] public IntPtr Copyright;
    [FieldOffset(48)] public nuint PortCount;
    [FieldOffset(56)] public IntPtr PortDescriptors;
    [FieldOffset(64)] public IntPtr PortNames;
    [FieldOffset(72)] public IntPtr PortRangeHints;

    // --- The Shift Happens Here ---
    // In your scan, 80 is NULL. We skip it and map Instantiate to 88.
    
    [FieldOffset(88)]  public IntPtr Instantiate;   
    [FieldOffset(96)]  public IntPtr ConnectPort;
    [FieldOffset(104)] public IntPtr Activate;      // Note: This is 0 in your scan, which is normal (Activate is optional)
    [FieldOffset(112)] public IntPtr Run;           // This maps to the ...1C20 pointer in your scan
    
    [FieldOffset(120)] public IntPtr RunAdding;
    [FieldOffset(128)] public IntPtr SetRunAddingGain;
    [FieldOffset(136)] public IntPtr Deactivate;
    [FieldOffset(144)] public IntPtr Cleanup;
}

public class LadspaLoader : IDisposable
{
    private IntPtr _libraryHandle;

    // Delegate matching: const LADSPA_Descriptor * ladspa_descriptor(unsigned long Index)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetDescriptorDelegate(uint index);

    public LadspaDescriptor? LoadDescriptor(string path, out IntPtr ptr) //uint index = 0)
    {
        uint index = 0;

        ptr = IntPtr.Zero; // def output

        // 1. Load the shared object file
        _libraryHandle = NativeLibrary.Load(path);
        
        // 2. Get the address of the discovery function
        IntPtr symbolAddr = NativeLibrary.GetExport(_libraryHandle, "ladspa_descriptor");
        var getDescriptor = Marshal.GetDelegateForFunctionPointer<GetDescriptorDelegate>(symbolAddr);

        // 3. Call the function to get a pointer to the struct
        IntPtr descriptorPtr = getDescriptor(index);

        if (descriptorPtr == IntPtr.Zero) return null;

        ptr = descriptorPtr;

        // 4. Marshal the pointer into our C# struct
        LadspaDescriptor r =  Marshal.PtrToStructure<LadspaDescriptor>(descriptorPtr);

        Console.WriteLine($"Ptr = {ptr}");

        return r;
    }


    public void Dispose()
    {
        if (_libraryHandle != IntPtr.Zero)
            NativeLibrary.Free(_libraryHandle);
    }
}

[Flags]
public enum PortDescriptor : uint
{
    Input = 0x1,
    Output = 0x2,
    Control = 0x4,
    Audio = 0x8
}

// JACK now -----------------------------------------

public interface IAudioProcessor
{
    void Process(ReadOnlySpan<float> input, Span<float> output);
}

public class JackToLadspa_SC1
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr InstantiateDelegate(IntPtr descriptor, nuint sampleRate);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ConnectPortDelegate(IntPtr instance, nuint port, IntPtr dataLocation);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ActivateDelegate(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RunDelegate(IntPtr instance, uint sampleCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void CleanupDelegate(IntPtr instance);

    // Port indices for sc1_1425.so
    public const uint SC1_ATTACK = 0;
    public const uint SC1_RELEASE = 1;
    public const uint SC1_THRESHOLD = 2;
    public const uint SC1_RATIO = 3;
    public const uint SC1_KNEE = 4;
    public const uint SC1_MAKEUP = 5;
    public const uint SC1_INPUT = 6;
    public const uint SC1_OUTPUT = 7;

    // Hold the delegates
    public RunDelegate _runPlugin;
    public ConnectPortDelegate _connectPort;
    public IntPtr _pluginHandle;

    // Control variables (must be pinned or unmanaged)
    private float[] _controls = new float[6]; 
    private GCHandle _controlsHandle;

    public void InitializePlugin(LadspaDescriptor d, IntPtr handle, int sampleRate)
    {
        ActivateDelegate? activate = null;
        if (d.Activate != IntPtr.Zero)
        {
            activate = Marshal.GetDelegateForFunctionPointer<ActivateDelegate>(d.Activate);
        }

        RunDelegate? run = null;
        if (d.Run != IntPtr.Zero)
        {
            run = Marshal.GetDelegateForFunctionPointer<RunDelegate>(d.Run);
        }
        else 
        {
            throw new Exception("Critical Error: Plugin has no Run() function!");
        }

        var instantiate = Marshal.GetDelegateForFunctionPointer<InstantiateDelegate>(d.Instantiate);
        _connectPort = Marshal.GetDelegateForFunctionPointer<ConnectPortDelegate>(d.ConnectPort);
        _runPlugin = Marshal.GetDelegateForFunctionPointer<RunDelegate>(d.Run);
        
        // 1. Create instance
        _pluginHandle = instantiate(handle, (nuint)sampleRate);

        // 2. Pin control array and connect control ports
        _controlsHandle = GCHandle.Alloc(_controls, GCHandleType.Pinned);
        IntPtr ctrlPtr = _controlsHandle.AddrOfPinnedObject();

        for (uint i = 0; i < 6; i++)
        {
            // Connect each control port to the offset in our pinned array
            _connectPort(_pluginHandle, i, ctrlPtr + (int)(i * sizeof(float)));
        }

        // Set some default values for SC1
        _controls[SC1_ATTACK] = 10.0f;    // ms
        _controls[SC1_RELEASE] = 100.0f;  // ms
        _controls[SC1_THRESHOLD] = -20.0f; // dB
        _controls[SC1_RATIO] = 4.0f;      // 4:1
        _controls[SC1_KNEE] = 5.0f;       // dB
        _controls[SC1_MAKEUP] = 0.0f;     // dB

        // 3. Activate if available
        activate?.Invoke(_pluginHandle);
    }

}