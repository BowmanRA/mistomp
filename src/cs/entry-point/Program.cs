using System.Runtime.InteropServices;
using System.Runtime.InteropServices;
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

foreach (var file in Directory.GetFiles(ladspaSettings.PluginDirectory, $"*{ladspaSettings.PluginFileExtension}"))
{
    Console.WriteLine($"\t{file}");
    ++pluginCount;

    var descriptor = loader.LoadDescriptor(file);

    if (descriptor.HasValue)
    {
        var d = descriptor.Value;
        string name = GetSafeString(d.Name);
        string maker = GetSafeString(d.Maker);

        Console.WriteLine($"\t\tFound Plugin: {name}");
        Console.WriteLine($"\t\tAuthor: {maker}");
        Console.WriteLine($"\t\tID: {d.UniqueID}");
        Console.WriteLine($"\t\tNumber of Ports: {d.PortCount}");

        for (var port = 0; port < d.PortCount; port++)
        {
            // Each port descriptor is a 32-bit uint
            uint portDescriptor = (uint)Marshal.ReadInt32(d.PortDescriptors, port * sizeof(uint));
            var pd = (PortDescriptor)portDescriptor;

            IntPtr portNamePtr = Marshal.ReadIntPtr(d.PortNames, port * IntPtr.Size);
            string portName = GetSafeString(portNamePtr);

            Console.Write($"\t\t\tPort {port} - {portName}: ");

            if (pd.HasFlag(PortDescriptor.Audio))
            {
                string direction = pd.HasFlag(PortDescriptor.Input) ? "Input" : "Output";
                Console.WriteLine($"Audio ({direction})");
            }
            else if (pd.HasFlag(PortDescriptor.Control))
            {
                Console.WriteLine("Control");
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
    for (int i = 0; i < d.PortCount; i++)
    {
        // 1. Get the descriptor bitmask for this port
        // PortDescriptors is an array of 32-bit uints
        uint mask = (uint)Marshal.ReadInt32(d.PortDescriptors, i * sizeof(uint));
        var type = (PortDescriptor)mask;

        // 2. Get the human-readable name of the port
        IntPtr namePtr = Marshal.ReadIntPtr(d.PortNames, i * IntPtr.Size);
        string portName = Marshal.PtrToStringAnsi(namePtr) ?? $"Port {i}";

        // 3. Categorize
        if (type.HasFlag(PortDescriptor.Audio))
        {
            string direction = type.HasFlag(PortDescriptor.Input) ? "In" : "Out";
            Console.WriteLine($"[AUDIO]  Index {i}: {portName} ({direction})");
        }
        else if (type.HasFlag(PortDescriptor.Control))
        {
            Console.WriteLine($"[KNOB]   Index {i}: {portName}");
        }
    }
}

// JACK --------------   see https://github.com/Beyley/LoudPizza/blob/main/LoudPizza.Backends.Jack2/JackBackend.cs
var jackTest = new JackTest();

jackTest.Test();

[DllImport("libjack.so.0", EntryPoint="jack_port_get_buffer")]
static extern unsafe void* JackPortGetBuffer(IntPtr port, uint nFrames);

class JackTest 
{
    public void Test() 
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

                int JackCallback(uint frames, void* usrData) 
                {
                    try 
                    {
                        // Console.Write($"{frames}");
                        // Console.Write($"{usrData}");
                        unsafe
                        {
                            //GCHandle    handle = GCHandle.FromIntPtr((IntPtr)usrData);
                            //JackBackend @jb  = (Jack.JackBackend)handle.Target!;
                            
                            //float* preInBuf = (float*)_jack.PortGetBuffer(inPortPre, frames);
                            //float* preOutBuf = (float*)_jack.PortGetBuffer(outPortPre, frames);


                            //float* postInBuf = (float*)_jack.PortGetBuffer(inPortPost, frames);
                            //float* postOutBuf = (float*)_jack.PortGetBuffer(outPortPost, frames);

                            // Console.Write(buf[0]);

                            //
                            // void* inPre = JackPortGetBuffer(inPort, frames);

                            for (int i = 0; i < frames; i++)
                            {
                                // preOutBuf[i] = preInBuf[i] * 0.05f;
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

[StructLayout(LayoutKind.Sequential)]
public struct LadspaDescriptor
{
    public nuint UniqueID;     // Use nuint for 'unsigned long' in C
    public IntPtr Label;       // char*
    public nuint Properties;   // Use nuint (8 bytes on 64-bit)
    public IntPtr Name;        // char*
    public IntPtr Maker;       // char*
    public IntPtr Copyright;   // char*
    public uint PortCount;     // uint is 4 bytes
    // There is usually 4 bytes of padding here to align the next pointer
    private uint _padding;     
    public IntPtr PortDescriptors;
    public IntPtr PortNames;
    public IntPtr PortRangeHints;
    public IntPtr Instantiate;
    public IntPtr ConnectPort;
    public IntPtr Activate;
    public IntPtr Run;
    public IntPtr RunAdding;
    public IntPtr SetRunAddingGain;
    public IntPtr Deactivate;
    public IntPtr Cleanup;
}


public class LadspaLoader : IDisposable
{
    private IntPtr _libraryHandle;
    
    // Delegate matching: const LADSPA_Descriptor * ladspa_descriptor(unsigned long Index)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetDescriptorDelegate(uint index);

    public LadspaDescriptor? LoadDescriptor(string path, uint index = 0)
    {
        // 1. Load the shared object file
        _libraryHandle = NativeLibrary.Load(path);
        
        // 2. Get the address of the discovery function
        IntPtr symbolAddr = NativeLibrary.GetExport(_libraryHandle, "ladspa_descriptor");
        var getDescriptor = Marshal.GetDelegateForFunctionPointer<GetDescriptorDelegate>(symbolAddr);

        // 3. Call the function to get a pointer to the struct
        IntPtr descriptorPtr = getDescriptor(index);

        if (descriptorPtr == IntPtr.Zero) return null;

        // 4. Marshal the pointer into our C# struct
        return Marshal.PtrToStructure<LadspaDescriptor>(descriptorPtr);
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
