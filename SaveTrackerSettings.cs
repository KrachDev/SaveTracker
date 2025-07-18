using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SaveTracker
{
    public class SaveTrackerSettings : ObservableObject
    {
        private bool showscosnoleoption;
        private bool autosync = true;
        private bool tackfiles = true;
        private bool trackreads;
        private bool trackwrites = true;
        private bool optionThatWontBeSaved;
        private bool track3rdparty = false;
        private CloudProvider selectedProvider = CloudProvider.GoogleDrive;

        public CloudProvider SelectedProvider 
        { 
            get => selectedProvider; 
            set => SetValue(ref selectedProvider, value); 
        }
        public int SelectedProviderIndex
        {
            get => (int)selectedProvider;
            set 
            { 
                SelectedProvider = (CloudProvider)value;
                OnPropertyChanged(); // or however you handle property change notifications
            }
        }
        public bool Track3rdParty { get => track3rdparty; set => SetValue(ref track3rdparty, value); }
        public bool ShowCosnoleOption { get => showscosnoleoption; set => SetValue(ref showscosnoleoption, value); }
        public bool TrackWrites { get => trackwrites; set => SetValue(ref trackwrites, value); }
        public bool TrackReads { get => trackreads; set => SetValue(ref trackreads, value); }
        public bool AutoSyncOption { get => autosync; set => SetValue(ref autosync, value); }
        public bool TrackFiles { get => tackfiles; set => SetValue(ref tackfiles, value); }
        // Playnite serializes settings object to a JSON object and saves it as text file.
        // If you want to exclude some property from being saved then use `JsonDontSerialize` ignore attribute.
        [DontSerialize]
        public bool OptionThatWontBeSaved { get => optionThatWontBeSaved; set => SetValue(ref optionThatWontBeSaved, value); }
    }

    public class SaveTrackerSettingsViewModel : ObservableObject, ISettings
    {
        private readonly SaveTracker plugin;
        private SaveTrackerSettings editingClone { get; set; }

        private SaveTrackerSettings settings;
        public SaveTrackerSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public SaveTrackerSettingsViewModel(SaveTracker plugin)
        {
            // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
            this.plugin = plugin;

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
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            Settings = editingClone;
        }

        public void EndEdit()
        {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            // This method should save settings made to Option1 and Option2.
            plugin.SavePluginSettings(Settings);
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