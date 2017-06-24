using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.IO;
using System.Text.RegularExpressions;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.AddIn;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.SharpDevelop.Editor;
using Microsoft.Msagl.Drawing;
using MoonSharp.Interpreter;
using ModifierKeys = System.Windows.Input.ModifierKeys;

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
		private CompletionWindow m_completionWindow;
		private TextMarkerService m_textMarkerService;
		private ToolTip m_errorTooltip = new ToolTip();
		private DispatcherTimer m_errorTimer;

		public Stream GenerateStreamFromString(string s)
		{
			var stream = new MemoryStream();
			var writer = new StreamWriter(stream);
			writer.Write(s);
			writer.Flush();
			stream.Position = 0;
			return stream;
		}

		public PassageEditor(MainWindow.JsonData.Passage data)
		{
			InitializeComponent();
			InitializeTextMarkerService();

			m_passage = data;
			TitleField.Text = data.title;
			TagsField.Text = String.Join(" ", data.tags);
			Editor.Text = data.body;

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
			Editor.TextArea.TextEntering += EditorOnTextEntering;
			Editor.TextArea.KeyDown += EditorOnKeyDown;
			Editor.MouseHover += TextEditorMouseHover;
			Editor.MouseHoverStopped += TextEditorMouseHoverStopped;

			m_errorTimer = new DispatcherTimer();
			m_errorTimer.Start();
			m_errorTimer.Interval = new TimeSpan(0,0,0,1);
			m_errorTimer.Tick += CheckForErrors;

			base.Closing += WindowClosing;
		}

		private void CheckForErrors(object sender, EventArgs e)
		{
			m_textMarkerService.RemoveAll(m => true);
			// parse for errors
			try
			{
				new Script().LoadString(Editor.Text);
			}
			catch (SyntaxErrorException exception)
			{
				ShowChunkError(exception.DecoratedMessage);
			}
			catch (Exception)
			{
				// bleep
				// we want to munch all errors here so the software doesn't crash
			}
		}

		void InitializeTextMarkerService()
		{
			var textMarkerService = new TextMarkerService(Editor.Document);
			Editor.TextArea.TextView.BackgroundRenderers.Add(textMarkerService);
			Editor.TextArea.TextView.LineTransformers.Add(textMarkerService);
			var services = (IServiceContainer)Editor.Document.ServiceProvider.GetService(typeof(IServiceContainer));
			if (services != null)
				services.AddService(typeof(ITextMarkerService), textMarkerService);
			m_textMarkerService = textMarkerService;
		}

		void TextEditorMouseHover(object sender, MouseEventArgs e)
		{
			var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor));
			if (pos != null)
			{
				var offset = Editor.Document.GetOffset(pos.Value.Location);
				foreach (var textMarker in m_textMarkerService.TextMarkers)
				{
					if (textMarker.StartOffset <= offset && textMarker.EndOffset >= offset)
					{
						m_errorTooltip.PlacementTarget = this; // required for property inheritance
						m_errorTooltip.IsOpen = true;
						e.Handled = true;
					}
				}
			}
		}

		void TextEditorMouseHoverStopped(object sender, MouseEventArgs e)
		{
			m_errorTooltip.IsOpen = false;
		}

		private void EditorOnKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
			{
				m_completionWindow = new CompletionWindow(Editor.TextArea);
				var data = m_completionWindow.CompletionList.CompletionData;

				data.Add(new MyCompletionData("action(message)"));
				data.Add(new MyCompletionData("action_option(passage, message, optional_lua)"));
				data.Add(new MyCompletionData("item(item, 1)"));
				data.Add(new MyCompletionData("stop()"));
				data.Add(new MyCompletionData("input(title, okfunc, cancelfunc)"));
				data.Add(new MyCompletionData("player(message)"));
				data.Add(new MyCompletionData("play_ambience(oggfile)"));
				data.Add(new MyCompletionData("play_music(oggfile)"));
				data.Add(new MyCompletionData("play_sound(oggfile)"));
				data.Add(new MyCompletionData("say(face, message)"));
				data.Add(new MyCompletionData("say_option(passage, message, optional_lua)"));
				data.Add(new MyCompletionData("stop_music()"));

				m_completionWindow.Show();
				m_completionWindow.Closed += delegate
				{
					m_completionWindow = null;
				};

				e.Handled = true;
			}
		}

		private void EditorOnTextEntering(object sender, TextCompositionEventArgs e)
		{
			if (e.Text.Length > 0 && m_completionWindow != null)
			{
				if (!char.IsLetterOrDigit(e.Text[0]))
				{
					// Whenever a non-letter is typed while the completion window is open,
					// insert the currently selected element.
					m_completionWindow.CompletionList.RequestInsertion(e);
				}
			}
		}

		private void EditorOnTextChanged(object sender, EventArgs eventArgs)
		{
			m_passage.body = Editor.Text;
			mainWindow.MadeChanges();
			m_errorTimer.Stop();
			m_errorTimer.Start();
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
			var missing = new List<string>();
			var links = mainWindow.GetLinks(m_passage.body);
			foreach (var link in links)
			{
				if (mainWindow.GetPassageCount(link) == 0)
				{
					missing.Add(link);
				}
			}

			if (missing.Count > 0)
			{
				var result = MessageBox.Show("There are new links to other passages. Create empty passages for these?", "Add New Passages",
					MessageBoxButton.YesNo);
				switch (result)
				{
					case MessageBoxResult.Yes:
						foreach (var title in missing)
						{
							mainWindow.AddPassage(title);
						}
						mainWindow.UpdateGraph();
						break;
				}
			}

			mainWindow.PassageUpdated();
			mainWindow.Activate();
		}

		private void ShowChunkError(string error)
		{
			
			const string regex = "\\((?<line>\\d+),(?<index>[\\d-]+)\\): (?<message>.+)";
			var rmatches = Regex.Matches(error, regex);
			if (rmatches.Count > 0)
			{
				var line = Convert.ToInt32(rmatches[0].Groups["line"].Captures[0].Value);
				var index = rmatches[0].Groups["index"].Captures[0].Value;
				int start = 0, end = 0;
				var i = index.IndexOf("-");
				if (i > -1)
				{
					start = Convert.ToInt32(index.Substring(0, i));
					end = Convert.ToInt32(index.Substring(i + 1));
				}
				else
				{
					start = Convert.ToInt32(index);
					end = -1;
				}
				var message = rmatches[0].Groups["message"].Captures[0].Value;
				ITextMarker marker;
				if (end > -1)
				{
					marker = m_textMarkerService.Create(Editor.Document.GetOffset(line, start), end - start);
				}
				else
				{
					var l = Editor.Document.GetLineByNumber(line);
					marker = m_textMarkerService.Create(l.Offset, l.Length);
				}
				marker.MarkerTypes = TextMarkerTypes.SquigglyUnderline;
				marker.MarkerColor = Colors.Red;
				m_errorTooltip.Content = message;
			}
		}
	}
}
