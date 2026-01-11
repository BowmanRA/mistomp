public class AppSettings
{
    public string Version { get; set; } = string.Empty;
    public LadspaSettings Ladspa { get; set; } = new LadspaSettings();
}   
public class LadspaSettings
{
    public string PluginDirectory { get; set; } = string.Empty;
    public string PluginFileExtension { get; set; } = string.Empty;
}