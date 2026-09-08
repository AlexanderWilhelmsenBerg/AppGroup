using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace AppGroup
{
    public class BackupGroupPreviewItem : INotifyPropertyChanged
    {
        public string GroupId { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public int ShortcutCount { get; set; }
        public string? GroupIcon { get; set; }
        public List<string> PathIcons { get; set; } = new();
        public int AdditionalIconsCount { get; set; }
        public string AdditionalIconsText => AdditionalIconsCount > 0 ? $"+{AdditionalIconsCount}" : string.Empty;

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed partial class BackupImportDialog : ContentDialog
    {
        private readonly ObservableCollection<BackupGroupPreviewItem> _items;
        private bool _updatingSelectAll;
        private bool _importConfirmed;

        public bool ImportConfirmed => _importConfirmed;

        public BackupImportDialog(List<BackupGroupPreviewItem> items)
        {
            InitializeComponent();
            _items = new ObservableCollection<BackupGroupPreviewItem>(items);
            foreach (BackupGroupPreviewItem item in _items)
            {
                item.PropertyChanged += (_, eventArgs) =>
                {
                    if (eventArgs.PropertyName == nameof(BackupGroupPreviewItem.IsSelected))
                    {
                        SyncSelectAllState();
                    }
                };
            }

            GroupListView.ItemsSource = _items;
            CloseButton.Click += (_, _) => Hide();
            ImportButton.Click += (_, _) =>
            {
                _importConfirmed = true;
                Hide();
            };
        }

        public HashSet<string> GetSelectedIds() =>
            _items.Where(item => item.IsSelected)
                .Select(item => item.GroupId)
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (_updatingSelectAll)
            {
                return;
            }

            foreach (BackupGroupPreviewItem item in _items)
            {
                item.IsSelected = true;
            }
        }

        private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_updatingSelectAll)
            {
                return;
            }

            foreach (BackupGroupPreviewItem item in _items)
            {
                item.IsSelected = false;
            }
        }

        private void SyncSelectAllState()
        {
            _updatingSelectAll = true;
            int selectedCount = _items.Count(item => item.IsSelected);
            SelectAllCheckBox.IsChecked = selectedCount == _items.Count
                ? true
                : selectedCount == 0 ? false : null;
            _updatingSelectAll = false;
        }
    }
}
