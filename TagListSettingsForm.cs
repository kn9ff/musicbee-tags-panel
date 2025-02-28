﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class TagListSettingsForm : Form
    {
        private Dictionary<string, TagListSettingsPanel> _tagPanels = new Dictionary<string, TagListSettingsPanel>();
        public SettingsManager SettingsStorage { get; set; }

        public TagListSettingsForm(SettingsManager settingsStorage)
        {
            InitializeComponent();
            InitializeDialogResults();
            InitializeVersionLabel();
            SettingsStorage = settingsStorage;
            PopulatePanelsFromSettings();
            InitializeToolTip();
            CheckAndDisplayNoMetaDataTypeMessage();
        }

        private void InitializeDialogResults()
        {
            BtnSaveSettings.DialogResult = DialogResult.OK;
            BtnDiscardSettings.DialogResult = DialogResult.Cancel;
        }

        private void InitializeVersionLabel()
        {
            VersionLbl.Text = $"Version: {typeof(TagListSettingsForm).Assembly.GetName().Version}";
            VersionLbl.ForeColor = Color.Black;
        }

        private void PopulatePanelsFromSettings()
        {
            foreach (var storage in SettingsStorage.TagsStorages.Values)
            {
                AddPanel(storage);
            }
        }

        private void InitializeToolTip()
        {
            toolTipAddTagPage.SetToolTip(BtnAddMetaDataTypeTabPage, Messages.AddTagPageTooltip);
        }

        private void AddPanel(TagsStorage storage)
        {
            var tagName = storage.GetTagName();
            if (_tagPanels.ContainsKey(tagName))
            {
                ShowWarning(Messages.TagListTagAlreadyExistsMessage);
                return;
            }

            var tagsPanelSettingsPanel = new TagListSettingsPanel(tagName, SettingsStorage);
            _tagPanels.Add(tagName, tagsPanelSettingsPanel);
            var tabPage = new TabPage(tagName);
            tabPage.Controls.Add(tagsPanelSettingsPanel);
            TabControlSettings.TabPages.Add(tabPage);
            tagsPanelSettingsPanel.SetUpPanelForFirstUse();
        }

        private void OnAddTagPageButtonClick(object sender, EventArgs e)
        {
            var usedTags = _tagPanels.Keys;
            using (var form = new TagListSelectorForm(usedTags))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    var metaDataType = form.GetSelectedMetaDataType();
                    if (!_tagPanels.ContainsKey(metaDataType))
                    {
                        var storage = new TagsStorage { MetaDataType = metaDataType };
                        AddPanel(storage);
                        CheckAndDisplayNoMetaDataTypeMessage();
                    }
                    else
                    {
                        ShowWarning(Messages.TagListTagAlreadyExistsMessage);
                    }
                }
            }
        }

        private void OnRemoveTagPageButtonClick(object sender, EventArgs e)
        {
            if (TabControlSettings.SelectedTab != null && ConfirmTagPageRemoval())
            {
                RemoveSelectedTab(TabControlSettings.SelectedTab);
                CheckAndDisplayNoMetaDataTypeMessage();
            }
        }

        private bool ConfirmTagPageRemoval()
        {
            return MessageBox.Show(Messages.TagListRemoveTagPageWarning, Messages.WarningTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private void RemoveSelectedTab(TabPage tabToRemove)
        {
            var tagName = tabToRemove.Text;
            TabControlSettings.TabPages.Remove(tabToRemove);
            SettingsStorage.RemoveTagStorage(tagName);
            _tagPanels.Remove(tagName);
        }

        private void LinkAbout_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ShowMessageBox($"{Messages.AboutMessage} {VersionLbl.Text}", Messages.AboutCaption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void LinkGitHub_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(Messages.GitHubLinkSettings);
            }
            catch (Exception ex)
            {
                ShowWarning($"Failed to open link: {ex.Message}");
            }
        }

        private void ShowWarning(string message)
        {
            MessageBox.Show($"{message}", $"{Messages.WarningTitle}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void ShowMessageBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            MessageBox.Show($"{text}", $"{caption}", buttons, icon);
        }

        private void CheckAndDisplayNoMetaDataTypeMessage()
        {
            if (_tagPanels.Count == 0)
            {
                ShowInformation(Messages.NoMetaDataTypesMessage);
            }
        }

        private void ShowInformation(string message)
        {
            MessageBox.Show($"{message}", $"{Messages.InformationTitle}", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}