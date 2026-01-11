using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct LadspaDescriptorArm
{
    public uint UniqueID;
    public IntPtr Label;
    public IntPtr Name;
    public IntPtr Maker;
    public IntPtr Copyright;
    public uint PortCount;
    public IntPtr PortDescriptors;
    public IntPtr PortNames;
    public IntPtr PortRangeHints;
    public IntPtr Instantiate;
    public IntPtr ConnectPort;
    public IntPtr Activate;
    public IntPtr Run;
    // --- DO NOT MISS THESE TWO ---
    public IntPtr RunAdding;       
    public IntPtr SetRunAddingGain;
    // -----------------------------
    public IntPtr Deactivate;
    public IntPtr Cleanup;
}

[StructLayout(LayoutKind.Sequential)]
public struct LadspaDescriptorIntel
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