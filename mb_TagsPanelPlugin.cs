﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface _mbApiInterface;
        private Logger _logger;
        private Control _tagsPanelControl;
        private TabControl _tabControl;
        private List<MetaDataType> _availableMetaTags = new List<MetaDataType>();
        private Dictionary<string, TagListPanel> _checklistBoxList = new Dictionary<string, TagListPanel>();
        private Dictionary<string, TabPage> _tabPageList = new Dictionary<string, TabPage>();
        private Dictionary<string, CheckState> _tagsFromFiles = new Dictionary<string, CheckState>();
        private SettingsManager _settingsManager;
        private TagManager _tagManager;
        private UIManager _uiManager;
        private bool _showTagsNotInList = false;
        private string _metaDataTypeName;
        private bool _sortAlphabetically = false;
        private PluginInfo _pluginInformation = new PluginInfo();
        private string[] _selectedFilesUrls = Array.Empty<string>();
        private bool _ignoreEventFromHandler = true;
        private bool _excludeFromBatchSelection = true;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            InitializeApi(apiInterfacePtr);
            _pluginInformation = CreatePluginInfo();
            InitializePluginComponents();
            return _pluginInformation;
        }

        private void InitializeApi(IntPtr apiInterfacePtr)
        {
            _mbApiInterface = new MusicBeeApiInterface();
            _mbApiInterface.Initialise(apiInterfacePtr);
        }

        private PluginInfo CreatePluginInfo()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return new PluginInfo
            {
                PluginInfoVersion = PluginInfoVersion,
                Name = Messages.PluginNamePluginInfo,
                Description = Messages.PluginDescriptionPluginInfo,
                Author = Messages.PluginAuthorPluginInfo,
                TargetApplication = Messages.PluginTargetApplicationPluginInfo,
                Type = PluginType.General,
                VersionMajor = (short)version.Major,
                VersionMinor = (short)version.Minor,
                Revision = 1,
                MinInterfaceVersion = MinInterfaceVersion,
                MinApiRevision = MinApiRevision,
                ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents | ReceiveNotificationFlags.TagEvents | ReceiveNotificationFlags.DataStreamEvents,
                ConfigurationPanelHeight = 0
            };
        }

        private void InitializePluginComponents()
        {
            InitializeUIManager();
            ClearCollections();
            InitializeLogger();
            InitializeSettingsManager();
            InitializeTagManager();
            LoadPluginSettings();
            InitializeMenu();
            EnsureTabControlInitialized();
            _logger.Info($"{nameof(InitializePluginComponents)} started");
        }

        private void InitializeUIManager()
        {
            _uiManager = new UIManager(_mbApiInterface, _checklistBoxList, _selectedFilesUrls, RefreshPanelTagsFromFiles);
        }

        private void ClearCollections()
        {
            _tagsFromFiles.Clear();
            _tabPageList.Clear();
            _showTagsNotInList = false;
        }

        private void InitializeSettingsManager()
        {
            _settingsManager = new SettingsManager(_mbApiInterface, _logger);
        }

        private void InitializeTagManager()
        {
            _tagManager = new TagManager(_mbApiInterface, _settingsManager);
        }

        private void EnsureTabControlInitialized()
        {
            if (_tabControl != null)
            {
                _tabControl.SelectedIndexChanged -= TabControlSelectionChanged;
                _tabControl.SelectedIndexChanged += TabControlSelectionChanged;
            }
        }

        public bool Configure(IntPtr panelHandle)
        {
            // save any persistent settings in a sub-folder of this path
            // panelHandle will only be set if you set _pluginInformation.ConfigurationPanelHeight to a non-zero value
            // keep in mind the panel width is scaled according to the font the user has selected
            // if _pluginInformation.ConfigurationPanelHeight is set to 0, you can display your own popup window
            ShowSettingsDialog();
            return true;
        }

        private void InitializeLogger()
        {
            _logger = new Logger(_mbApiInterface);
        }

        private void InitializeMenu()
        {
            _mbApiInterface.MB_AddMenuItem("mnuTools/Tags-Panel Settings", "Tags-Panel: Open Settings", OnSettingsMenuClicked);
        }

        private void LoadPluginSettings()
        {
            if (_settingsManager == null)
            {
                _logger.Error("SettingsManager is not initialized.");
                return;
            }

            try
            {
                _settingsManager.LoadSettingsWithFallback();
                UpdateSettingsFromTagsStorage();
                _logger.Info("Plugin settings loaded successfully.");
            }
            catch (IOException ioEx)
            {
                _logger.Error($"I/O error while loading plugin settings: {ioEx.Message}");
                ShowErrorMessage("An error occurred while accessing the settings file. Please check file permissions or disk space.");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.Error($"Unauthorized access during settings load: {uaEx.Message}");
                ShowErrorMessage("Insufficient permissions to access the settings file.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error while loading plugin settings: {ex.Message}");
                ShowErrorMessage("An unexpected error occurred while loading settings.");
            }
        }

        private void UpdateSettingsFromTagsStorage()
        {
            var tagsStorage = _settingsManager.GetFirstOne();

            if (tagsStorage != null)
            {
                _metaDataTypeName = tagsStorage.MetaDataType;
                _sortAlphabetically = tagsStorage.Sorted;
            }
            else
            {
                _logger.Warn("No TagsStorage found in SettingsManager.");
                _metaDataTypeName = string.Empty;
                _sortAlphabetically = false;
            }
        }

        private void ShowSettingsDialog()
        {
            var settingsCopy = _settingsManager.DeepCopy();
            using (var tagsPanelSettingsForm = new TagListSettingsForm(settingsCopy))
            {
                if (tagsPanelSettingsForm.ShowDialog() == DialogResult.OK)
                {
                    _settingsManager = tagsPanelSettingsForm.SettingsStorage;
                    SavePluginConfiguration();
                    UpdateTabControlVisibility();
                }
            }
        }

        private void ShowErrorMessage(string message)
        {
            MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }


        private void UpdateTabControlVisibility()
        {
            _tabControl.Visible = _tabControl.Controls.Count > 0;
        }

        private void SavePluginConfiguration()
        {
            _settingsManager.SaveAllSettings();
            ApplySortOrderFromSettings();

            // Update TagsStorage for each TagListPanel
            foreach (var tagPanel in _checklistBoxList.Values)
            {
                var tagName = tagPanel.Name;
                var newTagsStorage = _settingsManager.RetrieveTagsStorageByTagName(tagName);
                if (newTagsStorage != null)
                {
                    tagPanel.UpdateTagsStorage(newTagsStorage);
                }
            }

            RefreshPanelContent();
            LogConfigurationSaved();
        }

        private void ApplySortOrderFromSettings()
        {
            _sortAlphabetically = _settingsManager.GetFirstOne()?.Sorted ?? false;
        }

        private void LogConfigurationSaved()
        {
            _logger.Info("Plugin configuration saved.");
        }

        private void RefreshPanelContent()
        {
            RebuildTabPages();
            InvokeRefreshTagTableData();
        }

        private void RebuildTabPages()
        {
            ClearAllTagPages();
            PopulateTabPages();
        }

        private void AddTagPanelForVisibleTags(string tagName)
        {
            if (!_settingsManager.TagsStorages.TryGetValue(tagName, out var tagsStorage))
            {
                _logger.Error("tagsStorage is null");
                return;
            }

            var tabPage = new TabPage(tagName);
            var checkListBox = new TagListPanel(_mbApiInterface, _settingsManager, tagName, _tagsFromFiles);
            _checklistBoxList[tagName] = checkListBox; // Update the checklistBoxList with the new instance

            checkListBox.Dock = DockStyle.Fill;
            checkListBox.RegisterItemCheckEventHandler(TagCheckStateChanged);
            tabPage.Controls.Add(checkListBox);

            _tabPageList[tagName] = tabPage;
            _tabControl.TabPages.Add(tabPage);
        }

        private TabPage GetOrCreateTagPage(string tagName)
        {
            if (!_tabPageList.TryGetValue(tagName, out var tabPage))
            {
                tabPage = new TabPage(tagName);
                _tabPageList[tagName] = tabPage;
                _tabControl.TabPages.Add(tabPage);
            }
            _logger.Info($"{nameof(GetOrCreateTagPage)} returned {nameof(tabPage)} for {nameof(tagName)}: {tagName}");
            return tabPage;
        }

        private void PopulateTabPages()
        {
            _tabPageList.Clear();
            _checklistBoxList.Clear();

            if (_tabControl?.TabPages != null)
            {
                _tabControl.TabPages.Clear();

                foreach (var tagsStorage in _settingsManager.TagsStorages.Values)
                {
                    AddTagPanelForVisibleTags(tagsStorage.MetaDataType);
                }
            }
        }


        private void ApplyTagsToSelectedFiles(string[] fileUrls, CheckState selected, string selectedTag)
        {
            var metaDataType = GetActiveTabMetaDataType();
            if (metaDataType != 0)
            {
                _tagManager.SetTagsInFile(fileUrls, selected, selectedTag, metaDataType);
            }
        }

        public MetaDataType GetActiveTabMetaDataType()
        {
            return Enum.TryParse(_metaDataTypeName, true, out MetaDataType result) ? result : 0;
        }

        private TagsStorage GetCurrentTagsStorage()
        {
            MetaDataType metaDataType = GetActiveTabMetaDataType();
            TagsStorage tagsStorage = metaDataType != 0 ? _settingsManager.RetrieveTagsStorageByTagName(metaDataType.ToString()) : null;
            _logger.Info($"{nameof(GetCurrentTagsStorage)} returned {nameof(tagsStorage)} for {nameof(metaDataType)}: {metaDataType}");
            return tagsStorage;
        }

        private void ClearAllTagPages()
        {
            foreach (var tabPage in _tabPageList.Values)
            {
                foreach (var control in tabPage.Controls.OfType<TagListPanel>())
                {
                    control.Dispose();
                }
                tabPage.Controls.Clear();
            }
            _tabPageList.Clear();
            _tabControl?.TabPages.Clear();
        }

        private Dictionary<string, CheckState> GetTagsFromStorage(TagsStorage currentTagsStorage)
        {
            var allTagsFromSettings = currentTagsStorage.GetTags();
            var trimmedTagKeys = new HashSet<string>(allTagsFromSettings.Select(tag => tag.Key.Trim()));
            var data = new Dictionary<string, CheckState>(allTagsFromSettings.Count);

            foreach (var tagFromSettings in allTagsFromSettings)
            {
                string trimmedKey = tagFromSettings.Key.Trim();
                CheckState checkState = _tagsFromFiles.TryGetValue(trimmedKey, out CheckState state) ? state : CheckState.Unchecked;
                data[trimmedKey] = checkState;
            }

            return data;
        }

        private void UpdateTagsDisplayFromStorage()
        {
            TagsStorage currentTagsStorage = GetCurrentTagsStorage();
            if (currentTagsStorage == null)
            {
                _logger.Error($"{nameof(currentTagsStorage)} is null");
                return;
            }

            // Sort tags by their index
            var sortedTags = currentTagsStorage.GetTags().OrderBy(tag => tag.Value).ToDictionary(tag => tag.Key, tag => tag.Value);
            var data = GetTagsFromStorage(currentTagsStorage);
            var trimmedTagKeys = new HashSet<string>(data.Keys);

            AddTagsFromFiles(data, trimmedTagKeys);

            string tagName = currentTagsStorage.GetTagName();
            _uiManager.AddTagsToChecklistBoxPanel(tagName, data);
        }

        private void AddTagsFromFiles(Dictionary<string, CheckState> data, HashSet<string> trimmedTagKeys)
        {
            foreach (var tagFromFile in _tagsFromFiles)
            {
                if (!trimmedTagKeys.Contains(tagFromFile.Key))
                {
                    data[tagFromFile.Key] = tagFromFile.Value;
                }
            }
        }

        private void InvokeRefreshTagTableData()
        {
            if (_tagsPanelControl == null || _tagsPanelControl.IsDisposed) return;

            void Action() => UpdateTagsDisplayFromStorage();
            if (_tagsPanelControl.InvokeRequired)
            {
                _tagsPanelControl.Invoke((Action)Action);
            }
            else
            {
                Action();
            }
        }

        public void OnSettingsMenuClicked(object sender, EventArgs args)
        {
            ShowSettingsDialog();
        }

        private void TagCheckStateChanged(object sender, ItemCheckEventArgs e)
        {
            if (_excludeFromBatchSelection || _ignoreEventFromHandler)
            {
                return;
            }

            _ignoreEventFromHandler = true;
            try
            {
                CheckState newState = e.NewValue;
                string name = ((CheckedListBox)sender).Items[e.Index].ToString();

                ApplyTagsToSelectedFiles(_selectedFilesUrls, newState, name);
                _mbApiInterface.MB_RefreshPanels();
            }
            finally
            {
                _ignoreEventFromHandler = false;
            }
        }

        private void TabControlSelectionChanged(object sender, EventArgs e)
        {
            var tabControl = sender as TabControl;
            if (tabControl == null) return;

            var selectedTab = tabControl.SelectedTab;
            if (selectedTab == null || selectedTab.IsDisposed) return;

            var newMetaDataTypeName = selectedTab.Text;
            if (_metaDataTypeName != newMetaDataTypeName)
            {
                _metaDataTypeName = newMetaDataTypeName;
                _uiManager.SwitchVisibleTagPanel(_metaDataTypeName);

                // Refresh the panel tags from the selected files regardless of the number of selected files
                RefreshPanelTagsFromFiles(_selectedFilesUrls);

                // Update the UI with the current data in _tagsFromFiles
                UpdateTagsInPanelOnFileSelection();
            }

            var checkListBoxPanel = selectedTab.Controls.OfType<TagListPanel>().FirstOrDefault();
            if (checkListBoxPanel != null)
            {
                checkListBoxPanel.Refresh();
                checkListBoxPanel.Invalidate();
            }
        }

        private void SetPanelEnabled(bool enabled = true)
        {
            if (_tagsPanelControl == null || _tagsPanelControl.IsDisposed)
            {
                // Control is not available; cannot set enabled state
                return;
            }

            if (_tagsPanelControl.InvokeRequired)
            {
                _tagsPanelControl.Invoke(new Action(() => _tagsPanelControl.Enabled = enabled));
            }
            else
            {
                _tagsPanelControl.Enabled = enabled;
            }
        }

        private void UpdateTagsInPanelOnFileSelection()
        {
            if (_tagsPanelControl == null)
            {
                // Handle the null case appropriately, e.g., log an error or initialize the control
                return;
            }

            if (_tagsPanelControl.InvokeRequired)
            {
                _tagsPanelControl.Invoke(new Action(UpdateTagsInPanelOnFileSelection));
            }
            else
            {
                _ignoreEventFromHandler = true;
                _excludeFromBatchSelection = true;
                InvokeRefreshTagTableData();
                _ignoreEventFromHandler = false;
                _excludeFromBatchSelection = false;
            }
        }

        /// <summary>
        /// Sets _availableMetaTags from files contained within a panel based on filenames array
        /// </summary>
        /// <param name="filenames"></param>
        private void RefreshPanelTagsFromFiles(string[] filenames)
        {
            if (filenames == null || filenames.Length == 0)
            {
                _tagsFromFiles.Clear();
                UpdateTagsInPanelOnFileSelection();
                SetPanelEnabled(true);
                return;
            }

            var currentTagsStorage = GetCurrentTagsStorage();
            if (currentTagsStorage == null)
            {
                _logger.Error("Current TagsStorage is null");
                return;
            }

            _tagsFromFiles = _tagManager.CombineTagLists(filenames, currentTagsStorage);
            UpdateTagsInPanelOnFileSelection();
            SetPanelEnabled(true);
        }

        private bool ShouldClearTags(string[] filenames)
        {
            return filenames == null || filenames.Length == 0;
        }

        private void ClearTagsAndUpdateUI()
        {
            _tagsFromFiles.Clear();
            UpdateTagsInPanelOnFileSelection();
            SetPanelEnabled(true);
        }

        private void UpdateTagsFromFiles(string[] filenames)
        {
            var currentTagsStorage = GetCurrentTagsStorage();
            if (currentTagsStorage != null)
            {
                _tagsFromFiles = _tagManager.CombineTagLists(filenames, currentTagsStorage);
                UpdateTagsInPanelOnFileSelection();
                SetPanelEnabled(true);
            }
        }

        private void CreateTabPanel()
        {
            _tabControl = (TabControl)_mbApiInterface.MB_AddPanel(_tagsPanelControl, (PluginPanelDock)6);
            _tabControl.Dock = DockStyle.Fill;
            _tabControl.SelectedIndexChanged += TabControlSelectionChanged;

            if (_tabControl.TabPages.Count == 0)
            {
                PopulateTabPages();
            }
        }

        private void AddControls()
        {
            if (_tagsPanelControl.InvokeRequired)
            {
                _tagsPanelControl.Invoke(new Action(AddControls));
            }
            else
            {
                _tagsPanelControl.SuspendLayout();
                CreateTabPanel();
                _tagsPanelControl.Controls.Add(_tabControl);
                _tagsPanelControl.Enabled = false;
                _tagsPanelControl.ResumeLayout();
            }
        }

        /// <summary>
        /// MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        /// </summary>
        /// <param name="reason">The reason why MusicBee has closed the plugin.</param>
        public void Close(PluginCloseReason reason)
        {
            _logger?.Info(reason.ToString("G"));
            _logger?.Dispose();
            _tagsPanelControl?.Dispose();
            _tagsPanelControl = null;
            _logger = null;
        }


        public void Uninstall()
        {
            try
            {
                string settingsPath = _settingsManager.GetSettingsPath();
                if (File.Exists(settingsPath))
                {
                    File.Delete(settingsPath);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to delete settings file: {ex.Message}");
            }

            try
            {
                string logFilePath = _logger?.GetLogFilePath();
                if (!string.IsNullOrEmpty(logFilePath) && File.Exists(logFilePath))
                {
                    File.Delete(logFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to delete log file: {ex.Message}");
            }
        }

        /// <summary>
        /// Receive event notifications from MusicBee.
        /// You need to set _pluginInformation.ReceiveNotificationFlags = PlayerEvents to receive all notifications, and not just the startup event.
        /// </summary>
        /// <param name="sourceFileUrl"></param>
        /// <param name="type"></param>
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            if (_tagsPanelControl == null || type == NotificationType.ApplicationWindowChanged || GetActiveTabMetaDataType() == 0 || _ignoreEventFromHandler) return;

            bool isTagsChanging = type == NotificationType.TagsChanging;
            bool isTrackChanged = type == NotificationType.TrackChanged;

            if (isTagsChanging)
            {
                _excludeFromBatchSelection = true;
                _mbApiInterface.Library_CommitTagsToFile(sourceFileUrl);
            }

            // if (isTagsChanging || isTrackChanged)
            if (isTrackChanged)
            {
                _tagsFromFiles = _tagManager.UpdateTagsFromFile(sourceFileUrl, GetActiveTabMetaDataType());
                if (_showTagsNotInList)
                {
                    InvokeRefreshTagTableData();
                }
                else
                {
                    InvokeRefreshTagTableData();
                }
            }

            _excludeFromBatchSelection = false;
        }
        public int OnDockablePanelCreated(Control panel)
        {
            _tagsPanelControl = panel;
            EnsureControlCreated();
            AddControls();
            DisplaySettingsPrompt();
            RefreshTagDataIfHandleCreated();
            return 0;  //  = 0 indicates to MusicBee this control resizeable
        }

        private void EnsureControlCreated()
        {
            if (!_tagsPanelControl.Created)
            {
                _tagsPanelControl.CreateControl();
            }
        }

        private void DisplaySettingsPrompt()
        {
            _uiManager.DisplaySettingsPromptLabel(_tagsPanelControl, _tabControl, "No tags available. Please add tags in the settings.");
        }

        private void RefreshTagDataIfHandleCreated()
        {
            if (_tagsPanelControl.IsHandleCreated)
            {
                InvokeRefreshTagTableData();
            }
        }

        /// <summary>
        /// Event handler triggered by MusicBee when the user selects files in the library view.
        /// </summary>
        /// <param name="filenames">List of selected files.</param>
        public void OnSelectedFilesChanged(string[] filenames)
        {
            _selectedFilesUrls = filenames ?? Array.Empty<string>();

            if (_selectedFilesUrls.Any())
            {
                RefreshPanelTagsFromFiles(_selectedFilesUrls);
            }
            else
            {
                _tagsFromFiles.Clear();
                UpdateTagsInPanelOnFileSelection();
            }
            if (_tagsPanelControl != null)
            {
                UpdateTagsInPanelOnFileSelection();
            }
            SetPanelEnabled(true);
        }

        /// <summary>
        /// The presence of this function indicates to MusicBee that the dockable panel created above will show menu items when the panel header is clicked.
        /// </summary>
        /// <returns>Returns the list of ToolStripMenuItems that will be displayed.</returns>
        public List<ToolStripItem> GetMenuItems()
        {
            var menuItems = new List<ToolStripItem>
            {
                new ToolStripMenuItem("Tag-Panel Settings", null, OnSettingsMenuClicked),
            };
            return menuItems;
        }
    }
}