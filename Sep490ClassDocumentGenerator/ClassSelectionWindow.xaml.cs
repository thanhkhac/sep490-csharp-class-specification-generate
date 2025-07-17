using System.Windows;
using System.Windows.Controls;

namespace Sep490ClassDocumentGenerator
{
    public partial class ClassSelectionWindow : Window
    {
        public List<SelectableClass> FilteredClasses { get; private set; }
        public List<SelectableClass> AllClasses => _allClasses;
        
        private List<SelectableClass> _allClasses;
        private readonly string _outputPath;
        private readonly int _startIndex;
        

        public Action<List<ClassInfo>, string, int> OnGenerate { get; set; }


        public ClassSelectionWindow(List<ClassInfo> allClasses, string outputPath, int startIndex)
        {
            InitializeComponent();

            _allClasses = allClasses
                .Select(c => new SelectableClass { ClassInfo = c, IsSelected = true })
                .ToList();

            FilteredClasses = new List<SelectableClass>(_allClasses);
            ClassListView.ItemsSource = FilteredClasses;
            
            _outputPath = outputPath;
            _startIndex = startIndex;
            
            UpdateToggleButtonText();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text.Trim().ToLower();
            FilteredClasses = _allClasses
                .Where(c => c.ClassInfo.ClassName.ToLower().Contains(query))
                .ToList();
            ClassListView.ItemsSource = FilteredClasses;
            UpdateToggleButtonText();
        }

        private void ToggleAll_Click(object sender, RoutedEventArgs e)
        {
            // Kiểm tra xem có item nào chưa được select không
            bool hasUnselected = FilteredClasses.Any(item => !item.IsSelected);

            // Nếu có item chưa select thì select all, ngược lại deselect all
            foreach (var item in FilteredClasses) { item.IsSelected = hasUnselected; }

            ClassListView.Items.Refresh();
            UpdateToggleButtonText();
        }

        private void UpdateToggleButtonText()
        {
            if (FilteredClasses.Count == 0)
            {
                ToggleAllButton.Content = "Select All";
                ToggleAllButton.IsEnabled = false;
            }
            else
            {
                ToggleAllButton.IsEnabled = true;
                bool allSelected = FilteredClasses.All(item => item.IsSelected);
                ToggleAllButton.Content = allSelected ? "Deselect All" : "Select All";
            }
        }
        
        public List<ClassInfo> GetSelectedClasses()
        {
            return _allClasses
                .Where(c => c.IsSelected)
                .Select(c => c.ClassInfo)
                .ToList();
        }
        
        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            var selectedClasses = _allClasses
                .Where(c => c.IsSelected)
                .Select(c => c.ClassInfo)
                .ToList();

            if (!selectedClasses.Any())
            {
                MessageBox.Show("No classes selected.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Gọi delegate để thực hiện generation
            OnGenerate?.Invoke(selectedClasses, _outputPath, _startIndex);
        }
        
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class SelectableClass
    {
        public ClassInfo ClassInfo { get; set; }
        public bool IsSelected { get; set; }
        public string FullClassName => $"{ClassInfo?.Namespace}.{ClassInfo?.ClassName}";
    }

}