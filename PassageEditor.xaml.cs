using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace DialogEditorWPF
{
	/// <summary>
	/// Interaction logic for PassageEditor.xaml
	/// </summary>
	public partial class PassageEditor : Window
	{
		public MainWindow mainWindow;
		private MainWindow.JsonData.Passage m_passage;

		private static IHighlightingDefinition highlight;

		public Stream GenerateStreamFromString(string s)
		{
			var stream = new MemoryStream();
			var writer = new StreamWriter(stream);
			writer.Write(s);
			writer.Flush();
			stream.Position = 0;
			return stream;
		}

		public PassageEditor()
		{
			InitializeComponent();

			if (highlight == null)
			{
				using (var stream = GenerateStreamFromString(File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + "/syntax.xshd")))
				{
					using (var reader = new XmlTextReader(stream))
					{
						var xshd = HighlightingLoader.LoadXshd(reader);
						highlight = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
					}
				}
			}

			Editor.SyntaxHighlighting = highlight;
			Editor.WordWrap = true;

			TitleField.TextChanged += TitleFieldOnTextChanged;
			TagsField.TextChanged += TagsFieldOnTextChanged;
			Editor.TextChanged += EditorOnTextChanged;

			base.Closing += WindowClosing;
		}

		private void EditorOnTextChanged(object sender, EventArgs eventArgs)
		{
			m_passage.body = Editor.Text;
		}

		private void TagsFieldOnTextChanged(object sender, TextChangedEventArgs textChangedEventArgs)
		{
			m_passage.tags = TagsField.Text.Trim(' ').Split(' ');
		}

		private void TitleFieldOnTextChanged(object sender, TextChangedEventArgs textChangedEventArgs)
		{
			m_passage.title = TitleField.Text;
			if (mainWindow.GetPassageCount(m_passage.title) > 1)
			{
				MessageBox.Show("Another passage with this title already exists!", "Error");
			}
		}

		private void WindowClosing(object sender, CancelEventArgs e)
		{
			mainWindow.PassageUpdated();
		}

		public void LoadData(MainWindow.JsonData.Passage data)
		{
			m_passage = data;
			TitleField.Text = data.title;
			TagsField.Text = String.Join(" ", data.tags);
			Editor.Text = data.body;
		}
	}
}
