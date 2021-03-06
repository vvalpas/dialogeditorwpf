using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.GraphViewerGdi;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Layout.MDS;
using Microsoft.Win32;
using Newtonsoft.Json;
using Color = Microsoft.Msagl.Drawing.Color;
using Formatting = Newtonsoft.Json.Formatting;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using Path = System.IO.Path;
using Point = Microsoft.Msagl.Core.Geometry.Point;

namespace DialogEditorWPF
{
	public static class CustomCommands
	{
		public static readonly RoutedUICommand AddPassage = new RoutedUICommand("Add Passage", "Add Passage",
			typeof (CustomCommands),
			new InputGestureCollection() {new KeyGesture(Key.N, ModifierKeys.Shift | ModifierKeys.Control)});
		public static readonly RoutedUICommand DelPassage = new RoutedUICommand("Delete Passage", "Delete Passage",
			typeof(CustomCommands),
			new InputGestureCollection() { new KeyGesture(Key.Delete, ModifierKeys.None) });
		public static readonly RoutedUICommand MDS = new RoutedUICommand("MDS", "MDS",
			typeof(CustomCommands),
			new InputGestureCollection() { });
		public static readonly RoutedUICommand Sugiyama = new RoutedUICommand("Sugiyama", "Sugiyama",
			typeof(CustomCommands),
			new InputGestureCollection() { });
	}

	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public class JsonData
		{
			public class Passage
			{
				public string title = "New Passage";

				[JsonIgnore]
				[XmlElement("body")]
				public XmlCDataSection Message
				{
					get
					{
						var doc = new XmlDocument();
						return doc.CreateCDataSection(body);
					}
					set
					{
						body = value.Value;
					}
				}

				[XmlIgnore]
				public string body = string.Empty;
				public string[] tags = new string[0];
				public float x, y;
			}

			public List<Passage> passages = new List<Passage>();
		}

		private List<JsonData.Passage> m_passages = new List<JsonData.Passage>();
		private string m_currentlyOpenFile;
		private object m_selectedObject;
		public string version = "0.0.7";
		private bool m_viewMDS;
		private bool m_hasChanges;

		public MainWindow()
		{
			InitializeComponent();

			CreateGraph();

			/*AddPassageButton.IsEnabled = false;
			SaveButton.IsEnabled = false;
			SaveAsButton.IsEnabled = false;
			DeletePassageButton.IsEnabled = false;*/

			var args = Environment.GetCommandLineArgs();
			if (args.Length > 1)
			{
				var fileName = args[1];
				if (File.Exists(fileName))
				{
					var extension = Path.GetExtension(fileName);
					if (extension == ".dxml")
					{
						LoadFile(fileName);
					}
				}
			}
			else
			{
				Close_Executed(null, null);
			}

			gViewer.DoubleClick += OnDoubleClick;
			gViewer.Click += GViewerOnClick;

			Closing += OnClosing;
		}

		private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (m_hasChanges)
			{
				var result = MessageBox.Show("You have unsaved changes. Save before closing?", "Save before closing?",
					MessageBoxButton.YesNoCancel);
				switch (result)
				{
					case MessageBoxResult.Yes:
						Save(m_currentlyOpenFile);
						break;
					case MessageBoxResult.Cancel:
						e.Cancel = true;
						break;
				}
			}
		}

		private void GViewerOnClick(object sender, EventArgs eventArgs)
		{
			m_selectedObject = gViewer.SelectedObject;
			//DeletePassageButton.IsEnabled = m_selectedObject != null;
		}

		private void OnDoubleClick(object sender, EventArgs e)
		{
			if (m_selectedObject != null)
			{
				var selectedPassage = string.Empty;
				if (m_selectedObject is Edge)
					selectedPassage = (m_selectedObject as Edge).SourceNode.UserData.ToString();
				if (m_selectedObject is Node)
					selectedPassage = (m_selectedObject as Node).UserData.ToString();

				var win = new PassageEditor(m_passages.FirstOrDefault(x => x.title == selectedPassage));
				win.mainWindow = this;
				win.ShowInTaskbar = false;
				win.Owner = this;
				win.Show();
			}
		}

		public void LoadFile(string path)
		{
			m_currentlyOpenFile = path;
			Title = "DialogEditor (" + m_currentlyOpenFile + ")";

			if (path.Contains(".json"))
			{
				m_passages = JsonConvert.DeserializeObject<JsonData>(File.ReadAllText(path)).passages;
			}
			else if (path.Contains(".dxml"))
			{
				var x = new System.Xml.Serialization.XmlSerializer(typeof(JsonData));
				using (var stream = new FileStream(path, FileMode.Open))
				{
					m_passages = ((JsonData)x.Deserialize(stream)).passages;
				}
			}
			else
			{
				MessageBox.Show("Invalid file format", "Error");
			}

			/*AddPassageButton.IsEnabled = true;
			SaveButton.IsEnabled = true;
			SaveAsButton.IsEnabled = true;*/
			CreateGraph();
		}

		public void MadeChanges()
		{
			Title = "DialogEditor (" + m_currentlyOpenFile + "*)";
			m_hasChanges = true;
		}

		public void Save(string path)
		{
			var data = new JsonData
			{
				passages = m_passages
			};

			if (path.Contains(".dxml"))
			{
				var x = new System.Xml.Serialization.XmlSerializer(typeof(JsonData));
				using (var stream = new FileStream(path, FileMode.Create))
				{
					x.Serialize(stream, data);
				}
			} else
			{
				File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
			}

			m_currentlyOpenFile = path;
			Title = "DialogEditor (" + m_currentlyOpenFile + ")";
			m_hasChanges = false;
		}

		private void CreateGraph()
		{
			var graph = new Graph("graph");
			if (m_viewMDS)
			{
				graph.LayoutAlgorithmSettings = new MdsLayoutSettings();
			}
			else
			{
				graph.LayoutAlgorithmSettings = new SugiyamaLayoutSettings();
				graph.Attr.LayerDirection = LayerDirection.LR;
			}

			if (m_passages != null)
			{
				foreach (var passage in m_passages)
				{
					graph.AddNode(CreateNode(passage));
				}

				// parse links
				foreach (var passage in m_passages)
				{
					foreach (var link in GetLinks(passage.body))
					{
						if (m_passages.Any(x => x.title == link))
							graph.AddEdge(passage.title, link);
					}

					// also add edges with shorthand print
					var includes = GetIncludes(passage.body);
					foreach (var include in includes)
					{
						var edge = graph.AddEdge(include, passage.title);
						edge.Attr.Color = Microsoft.Msagl.Drawing.Color.CornflowerBlue;
					}
				}
			}

			gViewer.CurrentLayoutMethod = LayoutMethod.UseSettingsOfTheGraph;
			gViewer.Graph = graph;
		}

		private List<string> GetIncludes(string str)
		{
			return new List<string>();
			/*
			var list = new List<string>();
			var done = false;
			const string regex = @"<<\w+>>";
			while (!done)
			{
				done = true;
				var rmatches = Regex.Matches(str, regex);

				foreach (Match match in rmatches)
				{
					var left = str.Substring(0, match.Index);
					var tag = str.Substring(match.Index, match.Length);
					var right = str.Substring(match.Index + match.Length);
					var psg = m_passages.FirstOrDefault(x => x.title.Equals(tag.Substring(2, tag.Length - 4)));
					if (psg != null)
					{
						list.Add(psg.title);
						str = left + right;
						done = false;
						break;
					}
				}
			}
			return list;*/
		}

		private Node CreateNode(JsonData.Passage passage)
		{
			var color = new Color(255,255,255);
			if (passage.tags.Any(x => x.ToLowerInvariant() == "red"))
				color = new Color(255,0,0);
			if (passage.tags.Any(x => x.ToLowerInvariant() == "green"))
				color = new Color(0, 255, 0);
			if (passage.tags.Any(x => x.ToLowerInvariant() == "blue"))
				color = new Color(0, 0, 255);
			var node = new Node(passage.title) {UserData = passage.title, Attr = {FillColor = color}};
			return node;
		}

		private void Close_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = m_passages != null;
		}

		private void Close_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			m_passages = null;
			m_currentlyOpenFile = string.Empty;
			Title = "DialogEditor";
			CreateGraph();
		}

		private void MDS_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = true;
		}

		private void Sugiyama_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = true;
		}

		private void MDS_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			m_viewMDS = true;
			CreateGraph();
		}

		private void Sugiyama_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			m_viewMDS = false;
			CreateGraph();
		}

		private void New_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = true;
		}

		private void New_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			m_passages = new List<JsonData.Passage>();
			m_passages.Add(new JsonData.Passage { title = "Start" });

			m_currentlyOpenFile = string.Empty;
			Title = "DialogEditor";

			CreateGraph();
		}

		private void Save_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = m_passages != null;
		}

		private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			if (!string.IsNullOrEmpty(m_currentlyOpenFile))
			{
				Save(m_currentlyOpenFile);
			}
			else
			{
				SaveAs_Executed(null,null);
			}
		}

		private void SaveAs_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = m_passages != null;
		}

		private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			var saveDialog = new SaveFileDialog();
			saveDialog.Filter = "Dialog File (*.dxml)|*.dxml|Dialog JSON (*.json)|*.json|All files (*.*)|*.*";
			if (saveDialog.ShowDialog() == true)
			{
				Save(saveDialog.FileName);
			}
		}

		private void Open_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = true;
		}

		private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			var openFileDialog = new OpenFileDialog();
			openFileDialog.Filter = "Dialog XML (*.dxml)|*.dxml|Dialog JSON (*.json)|*.json|All files (*.*)|*.*";
			if (openFileDialog.ShowDialog() == true)
			{
				LoadFile(openFileDialog.FileName);
			}
		}

		private void AddPassage_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = m_passages != null;
		}

		private void AddPassage_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			var name = string.Empty;
			var i = 0;
			do
			{
				i++;
				name = "Unnamed " + i;
			} while (m_passages.Any(x => x.title == name));

			m_passages.Add(new JsonData.Passage { title = name });
			CreateGraph();
		}

		public void AddPassage(string name)
		{
			// dont add duplicates
			if (m_passages.Any(x => x.title == name))
				return;
			m_passages.Add(new JsonData.Passage { title = name });
		}

		public void UpdateGraph()
		{
			CreateGraph();
		}

		private void DelPassage_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = m_selectedObject != null;
		}

		private void DelPassage_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			if (m_selectedObject != null)
			{
				var selectedPassage = string.Empty;
				if (m_selectedObject is Edge)
					selectedPassage = (m_selectedObject as Edge).SourceNode.UserData.ToString();
				if (m_selectedObject is Node)
					selectedPassage = (m_selectedObject as Node).UserData.ToString();

				m_passages.RemoveAll(x => x.title == selectedPassage);
				CreateGraph();
			}
		}

		public IEnumerable<string> GetLinks(string body)
		{
			var links = new List<string>();
			var lines = body.Split('\n');

			foreach (var line in lines)
			{
				var regex = "(action_option|say_option|continue)\\([\"\'](?<match>[^\"]*)[\"\']";
				var rmatches = Regex.Matches(line, regex);
				if (rmatches.Count > 0)
				{
					var text = rmatches[0].Groups["match"].Captures[0].Value;
					links.Add(text);
				}

				regex = "continue_random\\((?<match>.+)\\)";
				rmatches = Regex.Matches(line, regex);
				if (rmatches.Count > 0)
				{
					var text = rmatches[0].Groups["match"].Captures[0].Value;
					regex = "[\"\'](?<match>[^\"]*)[\"\']";
					rmatches = Regex.Matches(text, regex);
					for (var i = 0; i < rmatches.Count; ++i)
					{
						links.Add(rmatches[i].Groups["match"].Captures[0].Value);
					}
				}
			}

			return links;
		}

		internal int GetPassageCount(string p)
		{
			return m_passages.Count(passage => passage.title == p);
		}

		internal void PassageUpdated()
		{
			CreateGraph();
		}
	}
}
