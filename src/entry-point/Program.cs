var appsettings = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var ladspaSettings = appsettings.GetSection("Ladspa").Get<LadspaSettings>();

Console.WriteLine($"Discovering LADSPA plugins in directory {ladspaSettings.PluginDirectory} as {ladspaSettings.PluginFileExtension} ...");
int pluginCount = 0;
foreach (var file in Directory.GetFiles(ladspaSettings.PluginDirectory, $"*{ladspaSettings.PluginFileExtension}"))
{
    Console.WriteLine($"\t{file}");
    ++pluginCount;
}
Console.WriteLine($"... found {pluginCount} LADSPA plugins.");

