using System.Text.Json;
using Serilog;

namespace PingTunnelVPN.Core;

/// <summary>
/// Manages loading and saving of VPN configurations.
/// </summary>
public class ConfigManager
{
    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PingTunnelVPN");

    private static readonly string ConfigsFilePath = Path.Combine(ConfigDirectory, "configs.json");
    private static readonly string GlobalSettingsFilePath = Path.Combine(ConfigDirectory, "global-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private List<ServerConfig> _configs = new();
    private Guid? _selectedConfigId;
    private GlobalSettings _globalSettings = new();
    private readonly object _lock = new();

    /// <summary>
    /// Event raised when configuration changes.
    /// </summary>
    public event EventHandler<VpnConfiguration>? ConfigurationChanged;

    /// <summary>
    /// Event raised when the selected config changes.
    /// </summary>
    public event EventHandler<ServerConfig?>? SelectedConfigChanged;

    public ConfigManager()
    {
        LoadConfigs();
        LoadGlobalSettings();
    }

    /// <summary>
    /// Gets the global settings.
    /// </summary>
    public GlobalSettings GlobalSettings
    {
        get
        {
            lock (_lock)
            {
                return _globalSettings.Clone();
            }
        }
    }

    /// <summary>
    /// Gets the current configuration (selected config or first available).
    /// </summary>
    public VpnConfiguration CurrentConfig
    {
        get
        {
            lock (_lock)
            {
                var selected = GetSelectedConfig();
                return selected?.Configuration ?? new VpnConfiguration();
            }
        }
    }

    /// <summary>
    /// Gets all server configurations.
    /// </summary>
    public List<ServerConfig> GetAllConfigs()
    {
        lock (_lock)
        {
            return _configs.Select(c => c.Clone()).ToList();
        }
    }

    /// <summary>
    /// Gets a specific configuration by ID.
    /// </summary>
    public ServerConfig? GetConfig(Guid id)
    {
        lock (_lock)
        {
            return _configs.FirstOrDefault(c => c.Id == id)?.Clone();
        }
    }

    /// <summary>
    /// Gets the currently selected configuration.
    /// </summary>
    public ServerConfig? GetSelectedConfig()
    {
        lock (_lock)
        {
            if (_selectedConfigId.HasValue)
            {
                return _configs.FirstOrDefault(c => c.Id == _selectedConfigId.Value)?.Clone();
            }
            
            // If no config is selected, return the first one or null
            return _configs.FirstOrDefault()?.Clone();
        }
    }

    /// <summary>
    /// Gets the ID of the currently selected configuration.
    /// </summary>
    public Guid? GetSelectedConfigId()
    {
        lock (_lock)
        {
            return _selectedConfigId;
        }
    }

    /// <summary>
    /// Sets the selected configuration.
    /// </summary>
    public void SetSelectedConfig(Guid id)
    {
        lock (_lock)
        {
            if (!_configs.Any(c => c.Id == id))
            {
                throw new ArgumentException($"Configuration with ID {id} not found", nameof(id));
            }

            _selectedConfigId = id;
            SaveConfigs();
            
            var selected = GetSelectedConfig();
            SelectedConfigChanged?.Invoke(this, selected);
            if (selected != null)
            {
                ConfigurationChanged?.Invoke(this, selected.Configuration.Clone());
            }
        }
    }

    /// <summary>
    /// Adds a new configuration.
    /// </summary>
    public ServerConfig AddConfig(ServerConfig config)
    {
        lock (_lock)
        {
            if (config.Id == Guid.Empty)
            {
                config.Id = Guid.NewGuid();
            }

            // Ensure unique name
            config.Name = EnsureUniqueName(config.Name);

            config.CreatedAt = DateTime.UtcNow;
            config.LastModified = DateTime.UtcNow;

            _configs.Add(config.Clone());
            SaveConfigs();

            // If this is the first config, select it
            if (_configs.Count == 1)
            {
                _selectedConfigId = config.Id;
                SaveConfigs();
            }

            Log.Information("Added configuration: {Name} ({Id})", config.Name, config.Id);
            return config;
        }
    }

    /// <summary>
    /// Updates an existing configuration.
    /// </summary>
    public void UpdateConfig(Guid id, Action<ServerConfig> updateAction)
    {
        lock (_lock)
        {
            var config = _configs.FirstOrDefault(c => c.Id == id);
            if (config == null)
            {
                throw new ArgumentException($"Configuration with ID {id} not found", nameof(id));
            }

            var clone = config.Clone();
            updateAction(clone);
            clone.LastModified = DateTime.UtcNow;

            // Ensure unique name (unless it's the same config)
            if (clone.Name != config.Name)
            {
                clone.Name = EnsureUniqueName(clone.Name, excludeId: id);
            }

            var index = _configs.FindIndex(c => c.Id == id);
            _configs[index] = clone;
            SaveConfigs();

            // If this is the selected config, notify listeners
            if (_selectedConfigId == id)
            {
                SelectedConfigChanged?.Invoke(this, clone);
                ConfigurationChanged?.Invoke(this, clone.Configuration.Clone());
            }

            Log.Information("Updated configuration: {Name} ({Id})", clone.Name, clone.Id);
        }
    }

    /// <summary>
    /// Deletes a configuration.
    /// </summary>
    public void DeleteConfig(Guid id)
    {
        lock (_lock)
        {
            var config = _configs.FirstOrDefault(c => c.Id == id);
            if (config == null)
            {
                throw new ArgumentException($"Configuration with ID {id} not found", nameof(id));
            }

            _configs.RemoveAll(c => c.Id == id);

            // If deleted config was selected, select another one or clear selection
            if (_selectedConfigId == id)
            {
                _selectedConfigId = _configs.FirstOrDefault()?.Id;
            }

            SaveConfigs();

            // Notify if selection changed
            if (_selectedConfigId.HasValue)
            {
                var selected = GetSelectedConfig();
                SelectedConfigChanged?.Invoke(this, selected);
                if (selected != null)
                {
                    ConfigurationChanged?.Invoke(this, selected.Configuration.Clone());
                }
            }
            else
            {
                SelectedConfigChanged?.Invoke(this, null);
                ConfigurationChanged?.Invoke(this, new VpnConfiguration());
            }

            Log.Information("Deleted configuration: {Name} ({Id})", config.Name, config.Id);
        }
    }

    /// <summary>
    /// Loads all configurations from storage.
    /// </summary>
    private void LoadConfigs()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(ConfigsFilePath))
                {
                    var json = File.ReadAllText(ConfigsFilePath);
                    var data = JsonSerializer.Deserialize<ConfigsData>(json, JsonOptions);
                    if (data != null)
                    {
                        _configs = data.Configs ?? new List<ServerConfig>();
                        _selectedConfigId = data.SelectedConfigId;
                        Log.Information("Loaded {Count} configurations from {Path}", _configs.Count, ConfigsFilePath);
                        return;
                    }
                }

                // No configs exist, create a default one
                CreateDefaultConfig();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load configurations");
                CreateDefaultConfig();
            }
        }
    }

    /// <summary>
    /// Loads global settings from storage.
    /// </summary>
    private void LoadGlobalSettings()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(GlobalSettingsFilePath))
                {
                    var json = File.ReadAllText(GlobalSettingsFilePath);
                    var settings = JsonSerializer.Deserialize<GlobalSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        _globalSettings = settings;
                        Log.Information("Loaded global settings from {Path}", GlobalSettingsFilePath);
                        return;
                    }
                }

                // No global settings file exists, use defaults and save them
                _globalSettings = new GlobalSettings();
                SaveGlobalSettings();
                Log.Information("Created default global settings file at {Path}", GlobalSettingsFilePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load global settings");
                _globalSettings = new GlobalSettings();
                // Try to save defaults even if loading failed
                try
                {
                    SaveGlobalSettings();
                    Log.Information("Created default global settings file after load error");
                }
                catch
                {
                    // Ignore save errors during error recovery
                }
            }
        }
    }

    /// <summary>
    /// Saves global settings to storage.
    /// </summary>
    private void SaveGlobalSettings()
    {
        try
        {
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

            var json = JsonSerializer.Serialize(_globalSettings, JsonOptions);
            File.WriteAllText(GlobalSettingsFilePath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save global settings");
            throw;
        }
    }

    /// <summary>
    /// Gets the global settings.
    /// </summary>
    public GlobalSettings GetGlobalSettings()
    {
        lock (_lock)
        {
            return _globalSettings.Clone();
        }
    }

    /// <summary>
    /// Updates the global settings.
    /// </summary>
    public void UpdateGlobalSettings(Action<GlobalSettings> updateAction)
    {
        lock (_lock)
        {
            var clone = _globalSettings.Clone();
            updateAction(clone);
            _globalSettings = clone;
            SaveGlobalSettings();
            Log.Information("Updated global settings");
        }
    }


    /// <summary>
    /// Creates a default configuration.
    /// </summary>
    private void CreateDefaultConfig()
    {
        var defaultConfig = new ServerConfig
        {
            Id = Guid.NewGuid(),
            Name = "Default",
            Configuration = new VpnConfiguration(),
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.UtcNow
        };

        _configs.Add(defaultConfig);
        _selectedConfigId = defaultConfig.Id;
        SaveConfigs();
        Log.Information("Created default configuration");
    }

    /// <summary>
    /// Saves all configurations to storage.
    /// </summary>
    private void SaveConfigs()
    {
        try
        {
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

            var data = new ConfigsData
            {
                Configs = _configs,
                SelectedConfigId = _selectedConfigId
            };

            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(ConfigsFilePath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save configurations");
            throw;
        }
    }

    /// <summary>
    /// Ensures a unique name by appending a number if necessary.
    /// </summary>
    private string EnsureUniqueName(string name, Guid? excludeId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "New Config";
        }

        var baseName = name;
        var counter = 1;
        var uniqueName = name;

        while (_configs.Any(c => c.Name == uniqueName && (excludeId == null || c.Id != excludeId.Value)))
        {
            uniqueName = $"{baseName} ({counter})";
            counter++;
        }

        return uniqueName;
    }

    /// <summary>
    /// Updates the current configuration and saves it.
    /// </summary>
    public void Update(Action<VpnConfiguration> updateAction)
    {
        lock (_lock)
        {
            var selected = GetSelectedConfig();
            if (selected == null)
            {
                // Create a default config if none exists
                CreateDefaultConfig();
                selected = GetSelectedConfig();
            }

            if (selected != null)
            {
                var config = selected.Configuration.Clone();
                updateAction(config);
                
                UpdateConfig(selected.Id, sc => sc.Configuration = config);
            }
        }
    }

    /// <summary>
    /// Saves the provided configuration to the current selected config.
    /// </summary>
    public void Save(VpnConfiguration config)
    {
        lock (_lock)
        {
            var selected = GetSelectedConfig();
            if (selected == null)
            {
                // Create a default config if none exists
                CreateDefaultConfig();
                selected = GetSelectedConfig();
            }

            if (selected != null)
            {
                UpdateConfig(selected.Id, sc => sc.Configuration = config);
            }
        }
    }

    /// <summary>
    /// Loads configuration from the default location.
    /// </summary>
    public VpnConfiguration Load()
    {
        lock (_lock)
        {
            return CurrentConfig;
        }
    }

    /// <summary>
    /// Exports configuration to the specified file path.
    /// </summary>
    public void Export(string filePath)
    {
        lock (_lock)
        {
            try
            {
                var selected = GetSelectedConfig();
                if (selected == null)
                {
                    throw new InvalidOperationException("No configuration selected");
                }

                var exportConfig = selected.Configuration.Clone();
                var json = JsonSerializer.Serialize(exportConfig, JsonOptions);
                File.WriteAllText(filePath, json);
                Log.Information("Exported configuration to {Path}", filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to export configuration to {Path}", filePath);
                throw;
            }
        }
    }

    /// <summary>
    /// Imports configuration from the specified file path as a new config.
    /// </summary>
    public ServerConfig Import(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<VpnConfiguration>(json, JsonOptions);
            if (config == null)
            {
                throw new InvalidDataException("Failed to deserialize configuration file.");
            }

            // Validate the imported configuration
            var errors = config.Validate();
            if (errors.Count > 0)
            {
                Log.Warning("Imported configuration has validation warnings: {Errors}", 
                    string.Join(", ", errors));
            }

            // Create a new ServerConfig from the imported config
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var serverConfig = new ServerConfig
            {
                Name = fileName,
                Configuration = config
            };

            var added = AddConfig(serverConfig);
            Log.Information("Imported configuration from {Path} as {Name}", filePath, added.Name);
            return added;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import configuration from {Path}", filePath);
            throw;
        }
    }

    /// <summary>
    /// Resets the selected configuration to defaults.
    /// </summary>
    public VpnConfiguration Reset()
    {
        lock (_lock)
        {
            var selected = GetSelectedConfig();
            if (selected != null)
            {
                UpdateConfig(selected.Id, sc => sc.Configuration = new VpnConfiguration());
                return GetSelectedConfig()?.Configuration ?? new VpnConfiguration();
            }

            var defaultConfig = new VpnConfiguration();
            CreateDefaultConfig();
            return defaultConfig;
        }
    }

    /// <summary>
    /// Resets global settings to defaults.
    /// </summary>
    public GlobalSettings ResetGlobalSettings()
    {
        lock (_lock)
        {
            _globalSettings = new GlobalSettings();
            SaveGlobalSettings();
            Log.Information("Reset global settings to defaults");
            return _globalSettings.Clone();
        }
    }

    /// <summary>
    /// Gets the configuration directory path.
    /// </summary>
    public static string GetConfigDirectory() => ConfigDirectory;

    /// <summary>
    /// Internal data structure for serialization.
    /// </summary>
    private class ConfigsData
    {
        public List<ServerConfig> Configs { get; set; } = new();
        public Guid? SelectedConfigId { get; set; }
    }
}
