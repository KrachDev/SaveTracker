using Playnite.SDK;
using Playnite.SDK.Data;
using System.Collections.Generic;

namespace SaveTracker
{
    public class SaveTrackerSettings : ObservableObject
    {
        private bool _showscosnoleoption;
        private bool _autosync = true;
        private bool _trackfiles = true;
        private bool _checkremotesave = true;
        private bool _trackreads;
        private bool _trackwrites = true;
        private bool _optionThatWontBeSaved;
        private bool _track3Rdparty;
        private bool _showupload = true;
        private bool _showdownload = true;
        private bool _encryptconfigs = true;
        private CloudProvider _selectedProvider = CloudProvider.GoogleDrive;

        public CloudProvider SelectedProvider 
        { 
            get => _selectedProvider; 
            set => SetValue(ref _selectedProvider, value); 
        }
        public int SelectedProviderIndex
        {
            get => (int)_selectedProvider;
            set 
            { 
                SelectedProvider = (CloudProvider)value;
                OnPropertyChanged(); // or however you handle property change notifications
            }
        }
        public bool Track3RdParty { get => _track3Rdparty; set => SetValue(ref _track3Rdparty, value); }
        public bool ShowConsoleOption { get => _showscosnoleoption; set => SetValue(ref _showscosnoleoption, value); }
        public bool TrackWrites { get => _trackwrites; set => SetValue(ref _trackwrites, value); }
        public bool TrackReads { get => _trackreads; set => SetValue(ref _trackreads, value); }
        public bool AutoSyncOption { get => _autosync; set => SetValue(ref _autosync, value); }
        public bool TrackFiles { get => _trackfiles; set => SetValue(ref _trackfiles, value); }
        public bool CheckRemoteSave { get => _checkremotesave; set => SetValue(ref _checkremotesave, value); }
        public bool ShowUpload { get => _showupload; set => SetValue(ref _showupload, value); }
        public bool ShowDownload { get => _showdownload; set => SetValue(ref _showdownload, value); }
        public bool EncryptConfigs { get => _encryptconfigs; set => SetValue(ref _encryptconfigs, value); }
        // Playnite serializes settings object to a JSON object and saves it as text file.
        // If you want to exclude some property from being saved then use `JsonDontSerialize` ignore attribute.
        [DontSerialize]
        public bool OptionThatWontBeSaved { get => _optionThatWontBeSaved; set => SetValue(ref _optionThatWontBeSaved, value); }
    }

    public class SaveTrackerSettingsViewModel : ObservableObject, ISettings
    {
        private readonly SaveTracker _plugin;
        private SaveTrackerSettings EditingClone { get; set; }

        private SaveTrackerSettings _settings;
        public SaveTrackerSettings Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                OnPropertyChanged();
            }
        }

        public SaveTrackerSettingsViewModel(SaveTracker plugin)
        {
            // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
            this._plugin = plugin;

            // Load saved settings.
            var savedSettings = plugin.LoadPluginSettings<SaveTrackerSettings>();

            // LoadPluginSettings returns null if no saved data is available.
            if (savedSettings != null)
            {
                Settings = savedSettings;
            }
            else
            {
                Settings = new SaveTrackerSettings();
            }
        }

        public void BeginEdit()
        {
            // Code executed when settings view is opened and user starts editing values.
            EditingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            Settings = EditingClone;
        }

        public void EndEdit()
        {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            // This method should save settings made to Option1 and Option2.
            _plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();
            return true;
        }
    }
}