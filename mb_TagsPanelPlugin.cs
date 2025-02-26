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
        private readonly Dictionary<string, TagListPanel> _checklistBoxList = new Dictionary<string, TagListPanel>();
        private readonly Dictionary<string, TabPage> _tabPageList = new Dictionary<string, TabPage>();
        private Dictionary<string, CheckState> _tagsFromFiles = new Dictionary<string, CheckState>();
        private SettingsManager _settingsManager;
        private TagManager _tagManager;
        private UIManager _uiManager;
        private bool _showTagsNotInList;
        private string _metaDataTypeName;
        private bool _sortAlphabetically;
        private readonly PluginInfo _pluginInformation = new PluginInfo();
        private string[] _selectedFilesUrls = Array.Empty<string>();
        private bool _ignoreEventFromHandler = true;
        private bool _excludeFromBatchSelection = true;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            InitializeApi(apiInterfacePtr);
            var pluginInformation = CreatePluginInfo();
            InitializePluginComponents();
            return pluginInformation;
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

        private void InitializeLogger()
        {
            _logger = new Logger(_mbApiInterface);
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
        private void InitializeMenu()
        {
            _mbApiInterface.MB_AddMenuItem("mnuTools/Tags-Panel Settings", "Tags-Panel: Open Settings", OnSettingsMenuClicked);
        }

        private void LoadPluginSettings()
        {
            if (_settingsManager == null)
            {
                _logger?.Error("SettingsManager is not initialized.");
                return;
            }

            try
            {
                _settingsManager.LoadSettingsWithFallback();
                UpdateSettingsFromTagsStorage();
                _logger?.Info("Plugin settings loaded successfully.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger?.Error($"File access error while loading plugin settings: {ex.GetType().Name} - {ex.Message}");
                ShowErrorMessage($"Unable to access settings file: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger?.Error($"Unexpected error in {nameof(LoadPluginSettings)}: {ex}");
                ShowErrorMessage("An unexpected error occurred while loading settings.");
            }
        }

        private void UpdateSettingsFromTagsStorage()
        {
            var tagsStorage = _settingsManager.RetrieveFirstTagsStorage();
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
            using (var settingsForm = new TagListSettingsForm(settingsCopy))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    // When the settings dialog is closed via "Save Settings", update and save the settings.
                    _settingsManager = settingsForm.SettingsStorage;
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
            if (_tabControl != null)
            {
                _tabControl.Visible = _tabControl.Controls.Count > 0;
            }
        }

        private void SavePluginConfiguration()
        {
            _settingsManager.SaveAllSettings();
            ApplySortOrderFromSettings();

            foreach (var tagPanel in _checklistBoxList.Values)
            {
                var tagName = tagPanel.Name;
                var newTagsStorage = _settingsManager.RetrieveTagsStorageByTagName(tagName);
                if (newTagsStorage != null)
                {
                    var data = GetTagsFromStorage(newTagsStorage);
                    tagPanel.UpdateTagsStorage(newTagsStorage, data);
                }
            }

            RefreshPanelContent();
            _logger.Info("Plugin configuration saved.");
        }

        private void ApplySortOrderFromSettings()
        {
            _sortAlphabetically = _settingsManager.RetrieveFirstTagsStorage()?.Sorted ?? false;
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
                _logger.Error("TagsStorage is null for tag: " + tagName);
                return;
            }

            var tabPage = new TabPage(tagName);
            var tagListPanel = new TagListPanel(_mbApiInterface, _settingsManager, tagName, _checklistBoxList, _selectedFilesUrls, RefreshPanelTagsFromFiles)
            {
                Dock = DockStyle.Fill
            };

            tagListPanel.RegisterItemCheckEventHandler(TagCheckStateChanged);
            tabPage.Controls.Add(tagListPanel);

            _checklistBoxList[tagName] = tagListPanel;
            _tabPageList[tagName] = tabPage;
            _tabControl.TabPages.Add(tabPage);
        }

        private TabPage GetOrCreateTagPage(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
            {
                _logger.Error($"{nameof(GetOrCreateTagPage)}: tagName parameter is null or empty");
                throw new ArgumentNullException(nameof(tagName));
            }

            try
            {
                if (_tabPageList == null || _tabControl == null)
                {
                    _logger.Error($"{nameof(GetOrCreateTagPage)}: Required controls not initialized");
                    throw new InvalidOperationException("TabPage collection or TabControl is not initialized");
                }

                TabPage tabPage;
                if (!_tabPageList.TryGetValue(tagName, out tabPage) || tabPage == null || tabPage.IsDisposed)
                {
                    _logger.Info($"{nameof(GetOrCreateTagPage)}: Creating new tab page for tag: {tagName}");
                    tabPage = new TabPage(tagName);
                    _tabPageList[tagName] = tabPage;
                    _tabControl.TabPages.Add(tabPage);
                }
                else
                {
                    _logger.Info($"{nameof(GetOrCreateTagPage)}: Using existing tab page for tag: {tagName}");
                }

                return tabPage;
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in {nameof(GetOrCreateTagPage)} for tag '{tagName}': {ex.Message}");
                throw;
            }
        }

        private void PopulateTabPages()
        {
            try
            {
                _logger.Info($"Starting {nameof(PopulateTabPages)}...");

                // Clear existing collections
                _tabPageList.Clear();
                _checklistBoxList.Clear();

                if (_tabControl == null || _tabControl.IsDisposed)
                {
                    _logger.Error($"{nameof(PopulateTabPages)}: TabControl is null or disposed");
                    return;
                }

                _tabControl.TabPages.Clear();

                // Exit early if no tags storage
                if (_settingsManager?.TagsStorages == null || !_settingsManager.TagsStorages.Any())
                {
                    _logger.Warn($"{nameof(PopulateTabPages)}: No tags storage available");
                    return;
                }

                // Add visible tag panels based on settings
                int addedCount = 0;
                foreach (var tagsStorage in _settingsManager.TagsStorages.Values)
                {
                    if (tagsStorage != null && !string.IsNullOrEmpty(tagsStorage.MetaDataType))
                    {
                        AddTagPanelForVisibleTags(tagsStorage.MetaDataType);
                        addedCount++;
                    }
                }

                _logger.Info($"{nameof(PopulateTabPages)}: Added {addedCount} tag panels");

                // Show first tab if any exist
                if (_tabControl.TabCount > 0)
                {
                    _tabControl.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error in {nameof(PopulateTabPages)}: {ex}");
            }
        }

        /// <summary>
        /// Applies the selected tag to all selected files with the specified check state.
        /// </summary>
        /// <param name="fileUrls">Array of file paths to process</param>
        /// <param name="selected">The check state to apply</param>
        /// <param name="selectedTag">The tag to apply or remove</param>
        /// <returns>True if the operation was successful, false otherwise</returns>
        private bool ApplyTagsToSelectedFiles(string[] fileUrls, CheckState selected, string selectedTag)
        {
            if (fileUrls == null || fileUrls.Length == 0 || string.IsNullOrWhiteSpace(selectedTag))
            {
                _logger?.Debug("ApplyTagsToSelectedFiles called with invalid parameters");
                return false;
            }

            try
            {
                var metaDataType = GetActiveTabMetaDataType();
                if (metaDataType == 0)
                {
                    _logger?.Error("ApplyTagsToSelectedFiles failed: Invalid metadata type");
                    return false;
                }

                _logger?.Info($"Applying tag '{selectedTag}' with state {selected} to {fileUrls.Length} files");
                _tagManager.SetTagsInFile(fileUrls, selected, selectedTag, metaDataType);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error applying tags to files: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Gets the MetaDataType enum value for the currently active tab.
        /// </summary>
        /// <returns>
        /// The MetaDataType enum value corresponding to the active tab name, or 0 (None) if 
        /// the tab name is invalid or cannot be parsed as a MetaDataType.
        /// </returns>
        public MetaDataType GetActiveTabMetaDataType()
        {
            if (string.IsNullOrEmpty(_metaDataTypeName))
            {
                _logger?.Debug("GetActiveTabMetaDataType: _metaDataTypeName is null or empty");
                return 0;
            }

            if (Enum.TryParse(_metaDataTypeName, true, out MetaDataType result))
            {
                return result;
            }
            else
            {
                _logger?.Warn($"GetActiveTabMetaDataType: Failed to parse '{_metaDataTypeName}' as MetaDataType");
                return 0;
            }
        }

        /// <summary>
        /// Gets the current TagsStorage object associated with the active tab.
        /// </summary>
        /// <returns>
        /// The TagsStorage object for the currently active metadata type,
        /// or null if not available or if the metadata type is invalid.
        /// </returns>
        private TagsStorage GetCurrentTagsStorage()
        {
            if (_settingsManager == null)
            {
                _logger?.Error("GetCurrentTagsStorage failed: SettingsManager is null");
                return null;
            }

            MetaDataType metaDataType = GetActiveTabMetaDataType();
            if (metaDataType == 0)
            {
                _logger?.Debug("GetCurrentTagsStorage: No valid metadata type selected");
                return null;
            }

            var tagName = metaDataType.ToString();
            var tagsStorage = _settingsManager.RetrieveTagsStorageByTagName(tagName);

            // Only log at Info level if a tag storage was found
            if (tagsStorage != null)
            {
                _logger?.Debug($"Retrieved TagsStorage for metaDataType: {metaDataType}");
            }
            else
            {
                _logger?.Warn($"No TagsStorage found for metaDataType: {metaDataType}");
            }

            return tagsStorage;
        }

        private void ClearAllTagPages()
        {
            _logger?.Debug($"ClearAllTagPages: Starting cleanup of {_tabPageList.Count} tab pages");

            // Safely clean up and dispose all TagListPanel controls in each tab
            foreach (var tabPage in _tabPageList.Values.Where(tp => tp != null && !tp.IsDisposed))
            {
                try
                {
                    // Find and dispose all TagListPanel controls
                    var panels = tabPage.Controls.OfType<TagListPanel>().ToList();
                    foreach (var panel in panels)
                    {
                        // Unregister any event handlers to prevent memory leaks
                        panel.UnregisterItemCheckEventHandler(TagCheckStateChanged);
                        panel.Dispose();
                    }

                    tabPage.Controls.Clear();
                    _logger?.Debug($"Cleared controls from tab page: {tabPage.Text}");
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Error cleaning up tab page {tabPage.Text}: {ex.Message}");
                }
            }

            // Clear collections
            _tabPageList.Clear();

            // Clear TabControl if it exists
            if (_tabControl != null && !_tabControl.IsDisposed)
            {
                _tabControl.TabPages.Clear();
                _logger?.Debug("Cleared all tab pages from TabControl");
            }
            else
            {
                _logger?.Warn("TabControl was null or disposed during cleanup");
            }

            _logger?.Debug("ClearAllTagPages: Cleanup completed");
        }

        private Dictionary<string, CheckState> GetTagsFromStorage(TagsStorage currentTagsStorage)
        {
            if (currentTagsStorage == null)
            {
                _logger?.Error($"{nameof(GetTagsFromStorage)}: Received null TagsStorage");
                return new Dictionary<string, CheckState>();
            }

            var allTags = currentTagsStorage.GetTags() ?? new Dictionary<string, int>();
            var data = new Dictionary<string, CheckState>(allTags.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var tag in allTags)
            {
                string trimmedKey = tag.Key?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(trimmedKey))
                {
                    data[trimmedKey] = _tagsFromFiles.TryGetValue(trimmedKey, out var state) ? state : CheckState.Unchecked;
                }
            }

            return data;
        }

        private void UpdateTagsDisplayFromStorage()
        {
            try
            {
                // Get current tags storage and validate
                var currentTagsStorage = GetCurrentTagsStorage();
                if (currentTagsStorage == null)
                {
                    _logger?.Error($"{nameof(UpdateTagsDisplayFromStorage)}: Current TagsStorage is null");
                    return;
                }

                // Get tag name from storage
                var tagName = currentTagsStorage.GetTagName();
                if (string.IsNullOrEmpty(tagName))
                {
                    _logger?.Error($"{nameof(UpdateTagsDisplayFromStorage)}: Tag name from storage is empty or null");
                    return;
                }

                _logger?.Debug($"Updating tag display for '{tagName}'");

                // Get tags from storage and merge with tags from files
                var data = GetTagsFromStorage(currentTagsStorage);
                MergeTagsFromFiles(data);

                // Update the appropriate panel if it exists
                if (_checklistBoxList?.TryGetValue(tagName, out var tagListPanel) == true && tagListPanel != null)
                {
                    UpdateTagListPanel(tagListPanel, currentTagsStorage, data);
                }
                else
                {
                    _logger?.Error($"TagListPanel for tag '{tagName}' not found in checklist box dictionary");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error in {nameof(UpdateTagsDisplayFromStorage)}: {ex.Message}", ex);
            }
        }

        private void MergeTagsFromFiles(Dictionary<string, CheckState> data)
        {
            if (_tagsFromFiles == null || data == null)
                return;

            foreach (var tagFromFile in _tagsFromFiles)
            {
                if (!string.IsNullOrWhiteSpace(tagFromFile.Key) && !data.ContainsKey(tagFromFile.Key))
                {
                    data[tagFromFile.Key] = tagFromFile.Value;
                }
            }
        }

        private void UpdateTagListPanel(TagListPanel tagListPanel, TagsStorage tagsStorage, Dictionary<string, CheckState> data)
        {
            if (tagListPanel == null)
                return;

            if (tagListPanel.InvokeRequired)
            {
                try
                {
                    tagListPanel.Invoke(new Action(() => tagListPanel.UpdateTagsStorage(tagsStorage, data)));
                    _logger?.Debug($"Updated tags via Invoke for panel '{tagsStorage.GetTagName()}'");
                }
                catch (ObjectDisposedException)
                {
                    _logger?.Warn($"TagListPanel was disposed while attempting to invoke UpdateTagsStorage");
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.Warn($"Failed to invoke UpdateTagsStorage: {ex.Message}");
                }
            }
            else
            {
                tagListPanel.UpdateTagsStorage(tagsStorage, data);
                _logger?.Debug($"Updated tags directly for panel '{tagsStorage.GetTagName()}'");
            }
        }

        /// <summary>
        /// Refreshes the tag table data in a thread-safe manner.
        /// </summary>
        /// <remarks>
        /// This method ensures that UI updates are performed on the UI thread
        /// and handles any exceptions that might occur during the update process.
        /// </remarks>
        private void InvokeRefreshTagTableData()
        {
            if (_tagsPanelControl == null || _tagsPanelControl.IsDisposed)
            {
                _logger?.Debug("RefreshTagTableData skipped: panel control is null or disposed");
                return;
            }

            try
            {
                if (_tagsPanelControl.InvokeRequired)
                {
                    _logger?.Debug("Dispatching UpdateTagsDisplayFromStorage to UI thread");
                    _tagsPanelControl.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            UpdateTagsDisplayFromStorage();
                        }
                        catch (Exception ex)
                        {
                            _logger?.Error($"Error in UI thread while updating tags display: {ex.Message}", ex);
                        }
                    }));
                }
                else
                {
                    _logger?.Debug("Directly updating tags display from storage");
                    UpdateTagsDisplayFromStorage();
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger?.Error($"Failed to invoke UpdateTagsDisplayFromStorage: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Unexpected error in {nameof(InvokeRefreshTagTableData)}: {ex.Message}", ex);
            }
        }

        public void OnSettingsMenuClicked(object sender, EventArgs args)
        {
            try
            {
                _logger?.Info("Settings menu clicked - opening settings dialog");

                // Ensure settings manager is initialized before showing dialog
                if (_settingsManager == null)
                {
                    _logger?.Error("Unable to open settings: SettingsManager is not initialized");
                    MessageBox.Show("Cannot open settings at this time. Please try again later.",
                                   "Settings Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Use cursor to indicate processing
                using (new CursorScope(Cursors.WaitCursor))
                {
                    ShowSettingsDialog();
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error opening settings dialog: {ex.Message}", ex);
                MessageBox.Show($"An error occurred while opening settings: {ex.Message}",
                               "Settings Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Helper class to temporarily change the cursor and restore it when done
        /// </summary>
        private class CursorScope : IDisposable
        {
            private readonly Cursor _previous;

            public CursorScope(Cursor cursor)
            {
                _previous = Cursor.Current;
                Cursor.Current = cursor;
            }

            public void Dispose()
            {
                Cursor.Current = _previous;
            }
        }

        /// <summary>
        /// Handles the check state change event for tag checkboxes.
        /// Updates the selected files with the appropriate tag state when a user checks or unchecks a tag.
        /// </summary>
        /// <param name="sender">The CheckedListBox that raised the event</param>
        /// <param name="e">Event arguments containing the item index and check state</param>
        private void TagCheckStateChanged(object sender, ItemCheckEventArgs e)
        {
            // Skip processing if batch operations are excluded or if we're already handling an event
            if (_excludeFromBatchSelection || _ignoreEventFromHandler)
                return;

            // Prevent recursive event handling by setting the ignore flag
            _ignoreEventFromHandler = true;
            try
            {
                // Ensure sender is a CheckedListBox and has valid items
                if (sender is CheckedListBox checkedListBox && e.Index >= 0 && e.Index < checkedListBox.Items.Count)
                {
                    var newState = e.NewValue;
                    var tagName = checkedListBox.Items[e.Index]?.ToString();

                    if (!string.IsNullOrEmpty(tagName) && _selectedFilesUrls != null && _selectedFilesUrls.Length > 0)
                    {
                        // Apply the tag change to all selected files
                        _logger?.Debug($"Applying tag '{tagName}' with state {newState} to {_selectedFilesUrls.Length} files");
                        ApplyTagsToSelectedFiles(_selectedFilesUrls, newState, tagName);

                        // Refresh UI to reflect changes
                        _mbApiInterface.MB_RefreshPanels();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error while changing tag check state: {ex.Message}", ex);
            }
            finally
            {
                // Always reset the ignore flag to prevent event handling from being permanently disabled
                _ignoreEventFromHandler = false;
            }
        }

        /// <summary>
        /// Handles the selection change event for the tab control.
        /// Updates the current metadata type and refreshes the panel contents.
        /// </summary>
        /// <param name="sender">The tab control that raised the event</param>
        /// <param name="e">Event arguments</param>
        private void TabControlSelectionChanged(object sender, EventArgs e)
        {
            try
            {
                // Validate the sender is a TabControl and has a valid selected tab
                if (!(sender is TabControl tabControl) || tabControl.SelectedTab == null || tabControl.SelectedTab.IsDisposed)
                {
                    _logger?.Debug("TabControlSelectionChanged: Invalid tab control or selected tab");
                    return;
                }

                string newMetaDataTypeName = tabControl.SelectedTab.Text;
                _logger?.Debug($"TabControlSelectionChanged: Tab changed to '{newMetaDataTypeName}'");

                // Only update if the metadata type has actually changed
                if (_metaDataTypeName != newMetaDataTypeName)
                {
                    _logger?.Info($"Switching metadata type from '{_metaDataTypeName}' to '{newMetaDataTypeName}'");
                    _metaDataTypeName = newMetaDataTypeName;

                    // Update the UI manager with the new visible tag panel
                    _uiManager.SwitchVisibleTagPanel(_metaDataTypeName);

                    // Refresh the tags from the selected files with the new metadata type
                    RefreshPanelTagsFromFiles(_selectedFilesUrls);

                    // Update the panel to reflect the current file selection
                    UpdateTagsInPanelOnFileSelection();
                }

                // Find and refresh the tag list panel in the selected tab
                var tagListPanel = tabControl.SelectedTab.Controls
                    .OfType<TagListPanel>()
                    .FirstOrDefault();

                if (tagListPanel != null)
                {
                    _logger?.Debug($"Refreshing tag list panel for tab '{newMetaDataTypeName}'");
                    tagListPanel.Refresh();
                }
                else
                {
                    _logger?.Debug($"No tag list panel found in tab '{newMetaDataTypeName}'");
                }
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error in TabControlSelectionChanged: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Enables or disables the tags panel control in a thread-safe manner.
        /// </summary>
        /// <param name="enabled">True to enable the panel, false to disable it.</param>
        /// <remarks>
        /// This method safely handles cross-thread UI updates by using Invoke when necessary.
        /// It also protects against operating on disposed controls.
        /// </remarks>
        private void SetPanelEnabled(bool enabled = true)
        {
            if (_tagsPanelControl == null || _tagsPanelControl.IsDisposed)
            {
                _logger?.Debug("SetPanelEnabled: Panel control is null or disposed");
                return;
            }

            try
            {
                if (_tagsPanelControl.InvokeRequired)
                {
                    _tagsPanelControl.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!_tagsPanelControl.IsDisposed)
                            {
                                _tagsPanelControl.Enabled = enabled;
                                _logger?.Debug($"Panel {(enabled ? "enabled" : "disabled")} via UI thread");
                            }
                        }
                        catch (ObjectDisposedException ex)
                        {
                            _logger?.Debug($"Panel was disposed during Invoke: {ex.Message}");
                        }
                    }));
                }
                else
                {
                    _tagsPanelControl.Enabled = enabled;
                    _logger?.Debug($"Panel directly {(enabled ? "enabled" : "disabled")}");
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger?.Warn($"Failed to set panel enabled state: {ex.Message}");
            }
        }

        private void UpdateTagsInPanelOnFileSelection()
        {
            if (_tagsPanelControl == null)
                return;

            if (_tagsPanelControl.InvokeRequired)
                _tagsPanelControl.Invoke(new Action(UpdateTagsInPanelOnFileSelection));
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
        /// Refreshes the tags panel with tag information from the selected files.
        /// </summary>
        /// <param name="filenames">Array of file URLs to process</param>
        private void RefreshPanelTagsFromFiles(string[] filenames)
        {
            try
            {
                _logger?.Debug($"Refreshing panel tags from {(filenames?.Length ?? 0)} files");

                // Handle case where no files are selected
                if (filenames == null || filenames.Length == 0)
                {
                    _logger?.Debug("No files selected, clearing tags");
                    _tagsFromFiles.Clear();
                    UpdateTagsInPanelOnFileSelection();
                    SetPanelEnabled(true);
                    return;
                }

                // Get current tags storage
                var currentTagsStorage = GetCurrentTagsStorage();
                if (currentTagsStorage == null)
                {
                    _logger?.Error("RefreshPanelTagsFromFiles: Current TagsStorage is null");
                    SetPanelEnabled(false);
                    return;
                }

                // Update tags from selected files
                _logger?.Debug($"Combining tag lists for {filenames.Length} files with metadata type: {currentTagsStorage.GetTagName()}");
                _tagsFromFiles = _tagManager.CombineTagLists(filenames, currentTagsStorage);

                // Update UI with refreshed tag data
                UpdateTagsInPanelOnFileSelection();
                SetPanelEnabled(true);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Error refreshing panel tags: {ex.Message}", ex);
                SetPanelEnabled(true); // Ensure panel is enabled even if an error occurs
            }
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
                return;
            }

            _tagsPanelControl.SuspendLayout();
            CreateTabPanel();
            _tagsPanelControl.Controls.Add(_tabControl);
            _tagsPanelControl.Enabled = false;
            _tagsPanelControl.ResumeLayout();
        }

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

        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            if (_tagsPanelControl == null || type == NotificationType.ApplicationWindowChanged ||
                GetActiveTabMetaDataType() == 0 || _ignoreEventFromHandler)
                return;

            bool isTagsChanging = type == NotificationType.TagsChanging;
            bool isTrackChanged = type == NotificationType.TrackChanged;

            if (isTagsChanging)
            {
                _excludeFromBatchSelection = true;
                _mbApiInterface.Library_CommitTagsToFile(sourceFileUrl);
            }

            if (isTrackChanged)
            {
                _tagsFromFiles = _tagManager.UpdateTagsFromFile(sourceFileUrl, GetActiveTabMetaDataType());
                InvokeRefreshTagTableData();
            }

            _excludeFromBatchSelection = false;
        }

        public int OnDockablePanelCreated(Control panel)
        {
            _tagsPanelControl = panel;
            EnsureControlCreated();
            AddControls();
            _uiManager.DisplaySettingsPromptLabel(_tagsPanelControl, _tabControl, "No tags available. Please add tags in the settings.");
            if (_tagsPanelControl.IsHandleCreated)
                InvokeRefreshTagTableData();

            return 0;
        }

        private void EnsureControlCreated()
        {
            if (!_tagsPanelControl.Created)
            {
                _tagsPanelControl.CreateControl();
            }
        }

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
            SetPanelEnabled(true);
        }

        public List<ToolStripItem> GetMenuItems()
        {
            return new List<ToolStripItem>
                {
                    new ToolStripMenuItem("Tag-Panel Settings", null, OnSettingsMenuClicked)
                };
        }
    }
}