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
using Microsoft.Msagl.Drawing;
using Microsoft.Win32;
using Newtonsoft.Json;
using Color = System.Drawing.Color;
using Formatting = Newtonsoft.Json.Formatting;
using MouseEventArgs = System.Windows.Forms.MouseEventArgs;
using Path = System.IO.Path;

namespace DialogEditorWPF
{
	

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
			}

			public List<Passage> passages = new List<Passage>();
		}

		private List<JsonData.Passage> m_passages = new List<JsonData.Passage>();
		private string m_currentlyOpenFile;
		private object m_selectedObject;

		public MainWindow()
		{
			InitializeComponent();

			CreateGraph();

			AddPassageButton.IsEnabled = false;
			SaveButton.IsEnabled = false;
			SaveAsButton.IsEnabled = false;
			DeletePassageButton.IsEnabled = false;

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

			gViewer.DoubleClick += OnDoubleClick;
			gViewer.Click += GViewerOnClick;
		}

		private void GViewerOnClick(object sender, EventArgs eventArgs)
		{
			m_selectedObject = gViewer.SelectedObject;
			DeletePassageButton.IsEnabled = m_selectedObject != null;
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

				var win = new PassageEditor();
				win.mainWindow = this;
				win.LoadData(m_passages.FirstOrDefault(x => x.title == selectedPassage));
				win.Show();
			}
		}

		public void LoadFile(string path)
		{
			m_currentlyOpenFile = path;

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

			AddPassageButton.IsEnabled = true;
			SaveButton.IsEnabled = true;
			SaveAsButton.IsEnabled = true;
			CreateGraph();
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
		}

		private void CreateGraph()
		{
			var graph = new Graph("graph");
			graph.Attr.LayerDirection = LayerDirection.LR;

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

			gViewer.Graph = graph;
		}

		private List<string> GetIncludes(string str)
		{
			var list = new List<string>();
			var done = false;
			var regex = @"<<\w+>>";
			while (!done)
			{
				done = true;
				var rmatches = Regex.Matches(str, regex);

				foreach (Match match in rmatches)
				{
					var left = str.Substring(0, match.Index);
					var tag = str.Substring(match.Index, match.Length);
					var right = str.Substring(match.Index + match.Length);
					var psg = m_passages.FirstOrDefault(x => x.title == tag.Substring(2, tag.Length - 4));
					if (psg != null)
					{
						list.Add(psg.title);
						str = left + psg.body.Trim('\n', '\r') + right;
						done = false;
						break;
					}
				}
			}
			return list;
		}

		private Node CreateNode(JsonData.Passage passage)
		{
			return new Node(passage.title){UserData = passage.title};
		}

		private void NewButton_Click(object sender, RoutedEventArgs e)
		{
			m_passages = new List<JsonData.Passage>();
			m_passages.Add(new JsonData.Passage {title = "Start"});

			AddPassageButton.IsEnabled = true;
			SaveButton.IsEnabled = true;
			SaveAsButton.IsEnabled = true;

			CreateGraph();
		}

		private void SaveButton_Click(object sender, RoutedEventArgs e)
		{
			Save(m_currentlyOpenFile);
		}

		private void SaveAsButton_Click(object sender, RoutedEventArgs e)
		{
			var saveDialog = new SaveFileDialog();
			saveDialog.Filter = "Dialog File (*.dxml)|*.dxml|Dialog JSON (*.json)|*.json|All files (*.*)|*.*";
			if (saveDialog.ShowDialog() == true)
			{
				Save(saveDialog.FileName);
			}
		}

		private void OpenButton_Click(object sender, RoutedEventArgs e)
		{
			var openFileDialog = new OpenFileDialog();
			openFileDialog.Filter = "Dialog XML (*.dxml)|*.dxml|Dialog JSON (*.json)|*.json|All files (*.*)|*.*";
			if (openFileDialog.ShowDialog() == true)
			{
				LoadFile(openFileDialog.FileName);
			}
		}

		private void AddPassageButton_Click(object sender, RoutedEventArgs e)
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

		private void DeletePassageButton_OnClick(object sender, RoutedEventArgs e)
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

		public static IEnumerable<string> GetLinks(string body)
		{
			var links = new List<string>();
			var lines = body.Split('\n');
			var newBody = "";

			foreach (var line in lines)
			{
				if (!line.Contains("[["))
				{
					newBody += line + "\n";
					continue;
				}

				string displayText, passageTitle, script = "";
				// [[foo|bar][foo = 123]]
				var start = line.IndexOf("[[", StringComparison.Ordinal) + 1;
				var end = line.LastIndexOf(']');

				var full = line.Substring(start, end - start);
				// full = [foo|bar][foo = 123]
				if (full.Contains("]["))
				{
					var chunks = full.Split(new[] { "][" }, StringSplitOptions.None);
					var left = chunks[0].Substring(1);
					script = chunks[1].Substring(0, chunks[1].Length - 1);
					// left = foo|bar
					// right = foo = 123
					displayText = left;
					passageTitle = left;

					if (left.Contains("|"))
					{
						var leftChunks = left.Split('|');
						displayText = leftChunks[0];
						passageTitle = leftChunks[1];
					}
				}
				else
				{
					full = full.Substring(1, full.Length - 2);
					displayText = full;
					passageTitle = full;

					if (full.Contains("|"))
					{
						var leftChunks = full.Split('|');
						displayText = leftChunks[0];
						passageTitle = leftChunks[1];
					}
				}

				links.Add(passageTitle);
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
