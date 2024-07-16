﻿// A MusicBee plugin that displays a panel with tabpages containing checklistboxes. The user can select _availableMetaTags from the checklistboxes and the plugin will update the _availableMetaTags in the selected files.
// The plugin also has a settings dialog that allows the user to define the _availableMetaTags and the order in which they are displayed.
// The plugin also has a logger that logs errors and information messages. The plugin also has a settings storage class that saves the settings to a file.
// The plugin also has a _availableMetaTags manipulation class that manipulates the _availableMetaTags in the selected files. The plugin also has a plugin info class that contains information _about the plugin.
// The plugin also has a _availableMetaTags storage class that contains the _availableMetaTags and the order in which they are displayed. The plugin also has a checklistbox panel class that contains a checklistbox and a style class that styles

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface _mbApiInterface;
        private Logger _logger;
        private Control _panel;
        private TabControl _tabControl;
        private List<MetaDataType> _availableMetaTags = new List<MetaDataType>();
        private Dictionary<string, TagListPanel> _checklistBoxList = new Dictionary<string, TagListPanel>();
        private Dictionary<string, TabPage> _tabPageList = new Dictionary<string, TabPage>();
        private Dictionary<string, CheckState> _tagsFromFiles = new Dictionary<string, CheckState>();
        private SettingsManager _settingsStorage;
        private TagManager _tagsManipulation;
        private UIManager _uiManager;
        private bool _showTagsNotInList = false;
        private string _metaDataTypeName;
        private bool _sortAlphabetically;
        private PluginInfo _about = new PluginInfo();
        private string[] _selectedFileUrls = Array.Empty<string>();
        private bool _ignoreEventFromHandler = true;
        private bool _excludeFromBatchSelection = true;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            InitializeApi(apiInterfacePtr);
            _about = CreatePluginInfo();
            InitializePluginComponents();
            return _about;
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
            _uiManager = new UIManager(_mbApiInterface, _checklistBoxList, _selectedFileUrls, RefreshPanelTagsFromFiles);
            _uiManager.SetDependencies(_checklistBoxList, _selectedFileUrls, RefreshPanelTagsFromFiles);

            _tagsFromFiles.Clear();
            _tabPageList.Clear();
            _showTagsNotInList = false;

            InitializeLogger();

            _settingsStorage = new SettingsManager(_mbApiInterface, _logger);
            _tagsManipulation = new TagManager(_mbApiInterface, _settingsStorage);

            LoadPluginSettings();

            InitializeMenu();

            _logger.Info($"{nameof(InitializePluginComponents)} started");
        }

        public bool Configure(IntPtr panelHandle)
        {
            // save any persistent settings in a sub-folder of this path
            // panelHandle will only be set if you set _about.ConfigurationPanelHeight to a non-zero value
            // keep in mind the panel width is scaled according to the font the user has selected
            // if _about.ConfigurationPanelHeight is set to 0, you can display your own popup window
            ShowSettingsDialog();
            return true;
        }

        private void InitializeLogger()
        {
            _logger = new Logger(_mbApiInterface);
        }

        private void InitializeMenu()
        {
            _mbApiInterface.MB_AddMenuItem("mnuTools/Tags-Panel Settings", "Tags-Panel: Open Settings", SettingsMenuClicked);
        }

        private void LoadPluginSettings()
        {
            _settingsStorage.LoadSettingsWithFallback();
            UpdateSettingsFromTagsStorage();
        }

        private void UpdateSettingsFromTagsStorage()
        {
            var tagsStorage = _settingsStorage.GetFirstOne();
            if (tagsStorage != null)
            {
                _metaDataTypeName = tagsStorage.MetaDataType;
                _sortAlphabetically = tagsStorage.Sorted;
            }
        }

        private void ShowSettingsDialog()
        {
            var settingsCopy = _settingsStorage.DeepCopy();
            using (var tagsPanelSettingsForm = new TagListSettingsForm(settingsCopy))
            {
                if (tagsPanelSettingsForm.ShowDialog() == DialogResult.OK)
                {
                    _settingsStorage = tagsPanelSettingsForm.SettingsStorage;
                    SavePluginConfiguration();
                    UpdateTabControlVisibility();
                }
            }
        }

        private void UpdateTabControlVisibility()
        {
            _tabControl.Visible = _tabControl.Controls.Count > 0;
        }

        public void SavePluginConfiguration()
        {
            _settingsStorage.SaveAllSettings();
            ApplySortOrderFromSettings();
            RefreshPanelContent();
            LogConfigurationSaved();
        }

        private void ApplySortOrderFromSettings()
        {
            _sortAlphabetically = _settingsStorage.GetFirstOne()?.Sorted ?? false;
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
            // Use _settingsStorage instance to access TagsStorages
            if (!_settingsStorage.TagsStorages.TryGetValue(tagName, out var tagsStorage))
            {
                _logger.Error("tagsStorage is null");
                return;
            }

            var tabPage = GetOrCreateTagPage(tagName);
            TagListPanel checkListBox = GetOrCreateCheckListBoxPanel(tagName);
            // Populate the checklistbox based on the checkbox state
            if (_showTagsNotInList)
            {
                checkListBox.PopulateChecklistBoxesFromData(GetCombinedTags(tagsStorage));
            }
            else
            {
                checkListBox.PopulateChecklistBoxesFromData(tagsStorage.GetTags());
            }

            checkListBox.Dock = DockStyle.Fill;
            checkListBox.RegisterItemCheckEventHandler(TagCheckStateChanged);

            if (!tabPage.Controls.Contains(checkListBox))
            {
                tabPage.Controls.Add(checkListBox);
            }
            checkListBox.Visible = true;
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
            if (_tabControl?.TabPages != null)
            {
                _tabControl.TabPages.Clear();
                foreach (var tagsStorage in _settingsStorage.TagsStorages.Values)
                {
                    AddTagPanelForVisibleTags(tagsStorage.MetaDataType);
                }
            }
        }

        private Dictionary<string, CheckState> GetCombinedTags(TagsStorage tagsStorage)
        {
            // Combine tags from settings and files
            var combinedTags = new Dictionary<string, CheckState>(tagsStorage.GetTags());
            foreach (var tagFromFile in _tagsFromFiles)
            {
                if (!combinedTags.ContainsKey(tagFromFile.Key))
                {
                    combinedTags[tagFromFile.Key] = tagFromFile.Value;
                }
            }

            return combinedTags;
        }

        /// <summary>
        /// Removes a tab from the panel.
        /// </summary>
        /// <param name="tabName"></param>
        /// <param name="tabPage"></param>

        private TagListPanel GetOrCreateCheckListBoxPanel(string tagName)
        {
            if (!_checklistBoxList.TryGetValue(tagName, out var checkListBox) || checkListBox.IsDisposed)
            {
                checkListBox = new TagListPanel(_mbApiInterface, _settingsStorage, tagName, _tagsFromFiles);
                _checklistBoxList[tagName] = checkListBox;
            }

            return checkListBox;
        }

        private void ApplyTagsToSelectedFiles(string[] fileUrls, CheckState selected, string selectedTag)
        {
            var metaDataType = GetActiveTabMetaDataType();
            if (metaDataType != 0)
            {
                _tagsManipulation.SetTagsInFile(fileUrls, selected, selectedTag, metaDataType);
            }
        }

        public MetaDataType GetActiveTabMetaDataType()
        {
            return Enum.TryParse(_metaDataTypeName, true, out MetaDataType result) ? result : 0;
        }


        private TagsStorage GetCurrentTagsStorage()
        {
            MetaDataType metaDataType = GetActiveTabMetaDataType();
            TagsStorage tagsStorage = metaDataType != 0 ? _settingsStorage.RetrieveTagsStorageByTagName(metaDataType.ToString()) : null;
            _logger.Info($"{nameof(GetCurrentTagsStorage)} returned {nameof(tagsStorage)} for {nameof(metaDataType)}: {metaDataType}");
            return tagsStorage;
        }

        private void ClearAllTagPages()
        {
            _tabPageList.Clear();
            _tabControl?.TabPages.Clear();
        }

        private void AddTagsToChecklistBoxPanel(string tagName, Dictionary<string, CheckState> tags)
        {
            if (_checklistBoxList.TryGetValue(tagName, out var checklistBoxPanel) && !checklistBoxPanel.IsDisposed && checklistBoxPanel.IsHandleCreated)
            {
                checklistBoxPanel.PopulateChecklistBoxesFromData(tags);
            }
        }

        private void UpdateTagsDisplayFromStorage()
        {
            TagsStorage currentTagsStorage = GetCurrentTagsStorage();
            if (currentTagsStorage == null)
            {
                _logger.Error($"{nameof(currentTagsStorage)} is null");
                return;
            }

            currentTagsStorage.SortByIndex();
            var allTagsFromSettings = currentTagsStorage.GetTags();

            string tagName = currentTagsStorage.GetTagName();
            var trimmedTagKeys = new HashSet<string>(allTagsFromSettings.Select(tag => tag.Key.Trim()));

            Dictionary<string, CheckState> data = new Dictionary<string, CheckState>(allTagsFromSettings.Count);
            foreach (var tagFromSettings in allTagsFromSettings)
            {
                string trimmedKey = tagFromSettings.Key.Trim();
                CheckState checkState = _tagsFromFiles.TryGetValue(trimmedKey, out CheckState state) ? state : CheckState.Unchecked;
                data[trimmedKey] = checkState;
            }

            foreach (var tagFromFile in _tagsFromFiles)
            {
                if (!trimmedTagKeys.Contains(tagFromFile.Key))
                {
                    data[tagFromFile.Key] = tagFromFile.Value;
                }
            }

            AddTagsToChecklistBoxPanel(tagName, data);
        }

        private void InvokeRefreshTagTableData()
        {
            if (_panel?.IsDisposed != false) return;

            void Action() => UpdateTagsDisplayFromStorage();
            if (_panel.InvokeRequired)
            {
                _panel.Invoke((Action)Action);
            }
            else
            {
                Action();
            }
        }

        public void SettingsMenuClicked(object sender, EventArgs args)
        {
            ShowSettingsDialog();
        }

        private void TagCheckStateChanged(object sender, ItemCheckEventArgs e)
        {
            if (_excludeFromBatchSelection || _ignoreEventFromHandler)
            {
                return;
            }

            CheckState newState = e.NewValue;
            CheckState currentState = ((CheckedListBox)sender).GetItemCheckState(e.Index);

            if (newState != currentState)
            {
                string name = ((CheckedListBox)sender).Items[e.Index].ToString();

                _ignoreEventFromHandler = true;
                ApplyTagsToSelectedFiles(_selectedFileUrls, newState, name);
                _mbApiInterface.MB_RefreshPanels();
                _ignoreEventFromHandler = false;
            }
        }

        private void TabControlSelectionChanged(Object sender, TabControlEventArgs e)
        {
            if (e.TabPage == null || e.TabPage.IsDisposed) return;

            var newMetaDataTypeName = e.TabPage.Text;
            if (_metaDataTypeName != newMetaDataTypeName)
            {
                _metaDataTypeName = newMetaDataTypeName;
                _uiManager.SwitchVisibleTagPanel(_metaDataTypeName);
                RefreshPanelTagsFromFiles(_selectedFileUrls);
                InvokeRefreshTagTableData();
            }

            var checkListBoxPanel = e.TabPage.Controls.OfType<TagListPanel>().FirstOrDefault();
            if (checkListBoxPanel != null)
            {
                UpdateTagsInPanelOnFileSelection();
                checkListBoxPanel.Refresh();
                checkListBoxPanel.Invalidate();
            }
        }

        private void SetPanelEnabled(bool enabled = true)
        {
            if (_panel.InvokeRequired)
            {
                _panel.Invoke(new Action(() => _panel.Enabled = enabled));
            }
            else
            {
                _panel.Enabled = enabled;
            }
        }

        private void UpdateTagsInPanelOnFileSelection()
        {
            _ignoreEventFromHandler = true;
            _excludeFromBatchSelection = true;
            InvokeRefreshTagTableData();
            _ignoreEventFromHandler = false;
            _excludeFromBatchSelection = false;
        }

        /// <summary>
        /// Sets _availableMetaTags from files contained within a panel based on filenames array
        /// </summary>
        /// <param name="filenames"></param>
        private void RefreshPanelTagsFromFiles(string[] filenames)
        {
            _tagsFromFiles.Clear();

            if (filenames == null || filenames.Length == 0)
            {
                return;
            }

            var currentTagsStorage = GetCurrentTagsStorage();
            if (currentTagsStorage != null)
            {
                _tagsFromFiles = _tagsManipulation.CombineTagLists(filenames, currentTagsStorage);
            }

            if (_panel != null)
            {
                UpdateTagsInPanelOnFileSelection();
            }
            SetPanelEnabled(true);
        }

        private void CreateTabPanel()
        {
            _tabControl = (TabControl)_mbApiInterface.MB_AddPanel(_panel, (PluginPanelDock)6);
            _tabControl.Dock = DockStyle.Fill;
            _tabControl.Selected += TabControlSelectionChanged;

            if (_tabControl.TabPages.Count == 0)
            {
                PopulateTabPages();
            }
        }

        private void AddControls()
        {
            _panel.SuspendLayout();
            CreateTabPanel();
            _panel.Controls.Add(_tabControl);
            _panel.Enabled = false;
            _panel.ResumeLayout();
        }

        /// <summary>
        /// MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        /// </summary>
        /// <param name="reason">The reason why MusicBee has closed the plugin.</param>
        public void Close(PluginCloseReason reason)
        {
            _logger?.Info(reason.ToString("G"));
            _logger?.Dispose();
            _panel?.Dispose();
            _panel = null;
            _logger = null;
        }

        /// <summary>
        /// uninstall this plugin - clean up any persisted files
        /// </summary>
        public void Uninstall()
        {
            // Delete settings file
            if (System.IO.File.Exists(_settingsStorage.GetSettingsPath()))
            {
                System.IO.File.Delete(_settingsStorage.GetSettingsPath());
            }

            // Delete _logger file
            if (System.IO.File.Exists(_logger.GetLogFilePath()))
            {
                System.IO.File.Delete(_logger.GetLogFilePath());
            }
        }

        /// <summary>
        /// Receive event notifications from MusicBee.
        /// You need to set _about.ReceiveNotificationFlags = PlayerEvents to receive all notifications, and not just the startup event.
        /// </summary>
        /// <param name="sourceFileUrl"></param>
        /// <param name="type"></param>
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            if (_panel == null || type == NotificationType.ApplicationWindowChanged || GetActiveTabMetaDataType() == 0 || _ignoreEventFromHandler) return;

            bool isTagsChanging = type == NotificationType.TagsChanging;
            bool isTrackChanged = type == NotificationType.TrackChanged;

            if (isTagsChanging)
            {
                _excludeFromBatchSelection = true;
                _mbApiInterface.Library_CommitTagsToFile(sourceFileUrl);
            }

            if (isTagsChanging || isTrackChanged)
            {
                _tagsFromFiles = _tagsManipulation.UpdateTagsFromFile(sourceFileUrl, GetActiveTabMetaDataType());
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

        /// <summary>
        /// Event handler that is triggered by MusicBee when a dockable panel has been created.
        /// </summary>
        /// <param name="panel">A reference to the new panel.</param>
        /// <returns>
        /// &lt; 0 indicates to MusicBee this control is resizable and should be sized to fill the panel it is docked to in MusicBee<br/>
        ///  = 0 indicates to MusicBee this control resizeable<br/>
        /// &gt; 0 indicates to MusicBee the fixed height for the control. Note it is recommended you scale the height for high DPI screens(create a graphics object and get the DpiY value)
        /// </returns>
        public int OnDockablePanelCreated(Control panel)
        {
            _panel = panel;

            if (!_panel.Created)
            {
                _panel.CreateControl();
            }

            AddControls();
            _uiManager.DisplaySettingsPromptLabel(_panel, _tabControl, "No tags available. Please add tags in the settings.");
            if (_panel.IsHandleCreated)
            {
                InvokeRefreshTagTableData();
            }

            return 0;
        }

        /// <summary>
        /// Event handler triggered by MusicBee when the user selects files in the library view.
        /// </summary>
        /// <param name="filenames">List of selected files.</param>
        public void OnSelectedFilesChanged(string[] filenames)
        {
            _selectedFileUrls = filenames ?? Array.Empty<string>();

            if (_selectedFileUrls.Any())
            {
                RefreshPanelTagsFromFiles(_selectedFileUrls);
            }
            else
            {
                _tagsFromFiles.Clear();
                UpdateTagsInPanelOnFileSelection();
            }
            if (_panel != null)
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
                new ToolStripMenuItem("Tag-Panel Settings", null, SettingsMenuClicked),
            };
            return menuItems;
        }
    }
}