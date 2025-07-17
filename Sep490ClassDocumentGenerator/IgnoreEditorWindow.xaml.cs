using System.Windows;

namespace Sep490ClassDocumentGenerator
{
    public partial class IgnoreEditorWindow : Window
    {
        private readonly IgnoreManager _ignoreManager;

        public IgnoreEditorWindow(IgnoreManager ignoreManager)
        {
            InitializeComponent();
            _ignoreManager = ignoreManager;
            RefreshList();
        }

        private void RefreshList()
        {
            IgnoreListBox.ItemsSource = null;
            IgnoreListBox.ItemsSource = _ignoreManager.Rules.ToList();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var rule = NewRuleTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(rule))
            {
                _ignoreManager.AddRule(rule);
                _ignoreManager.SaveRules();
                RefreshList();
                NewRuleTextBox.Clear();
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreListBox.SelectedItem is string selected)
            {
                _ignoreManager.RemoveRule(selected);
                _ignoreManager.SaveRules();
                RefreshList();
            }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (IgnoreListBox.SelectedItem is string selected)
            {
                var input = Microsoft.VisualBasic.Interaction.InputBox("Edit rule:", "Edit Rule", selected);
                if (!string.IsNullOrEmpty(input) && input != selected)
                {
                    _ignoreManager.ReplaceRule(selected, input);
                    _ignoreManager.SaveRules();
                    RefreshList();
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}