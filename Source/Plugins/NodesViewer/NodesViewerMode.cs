#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Rendering;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder.Plugins.NodesViewer
{
	[EditMode(DisplayName = "Nodes Viewer Mode",
			  SwitchAction = "nodesviewermode",
			  ButtonImage = "NodesView.png",
			  ButtonOrder = 350,
			  ButtonGroup = "002_tools",
			  Volatile = true,
			  UseByDefault = true,
			  AllowCopyPaste = false)]
	public class NodesViewerMode : ClassicMode
	{
		#region ================== Constants

		private const float EPSILON = 0.00001f;
		
		#endregion
		
		#region ================== Variables

		private Seg[] segs;
		private Node[] nodes;
		private Vector2D[] verts;
		private Subsector[] ssectors;
		private readonly List<PixelColor> distinctcolors;
		private NodesForm form;
		private int mouseinssector = -1;
		private string nodesformat = "Classic nodes";
		private FlatVertex[] overviewgrayvertices;
		private FlatVertex[] overviewcoloredvertices;

		#endregion

		#region ================== Properties

		public Seg[] Segs { get { return segs; } }
		public Node[] Nodes { get { return nodes; } }
		public Vector2D[] Vertices { get { return verts; } }
		public Subsector[] Subsectors { get { return ssectors; } }
		public NodesForm Form { get { return form; } }
		
		public override bool AlwaysShowVertices { get { return true;  } }

		#endregion

		#region ================== Constructor / Destructor

		// Constructor
		public NodesViewerMode()
		{
			// Make a list of distict colors we can use to
			// display multiple things on the screen
			// Note that black and white are not in this list, because
			// these are the most likely colors for the user's background
			distinctcolors = new List<PixelColor> {
				PixelColor.FromColor(Color.Blue), 
				PixelColor.FromColor(Color.Orange), 
				PixelColor.FromColor(Color.ForestGreen), 
				PixelColor.FromColor(Color.Sienna), 
				PixelColor.FromColor(Color.LightPink), 
				PixelColor.FromColor(Color.Purple),
				PixelColor.FromColor(Color.Cyan), 
				PixelColor.FromColor(Color.LawnGreen), 
				PixelColor.FromColor(Color.PaleGoldenrod), 
				PixelColor.FromColor(Color.Red), 
				PixelColor.FromColor(Color.Yellow), 
				PixelColor.FromColor(Color.LightSkyBlue), 
				PixelColor.FromColor(Color.DarkGray), 
				PixelColor.FromColor(Color.Magenta)
			};
		}

		#endregion

		#region ================== Methods

		/// <summary>
		/// This loads all nodes structures data from the lumps
		/// </summary>
		private bool LoadClassicStructures()
		{
			List<byte[]> unsupportedheaders = new List<byte[]>() { Encoding.ASCII.GetBytes("ZNOD"), Encoding.ASCII.GetBytes("XNOD") };

			// Load the nodes structure
			MemoryStream nodesstream = General.Map.GetLumpData("NODES");

			if(nodesstream.Length < 4)
			{
				MessageBox.Show("The NODES lump is too short.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				General.Editing.CancelMode();
				return false;
			}

			BinaryReader nodesreader = new BinaryReader(nodesstream);

			// Compare the byte arrays. We can't do it by comparing strings, since the data read from the NODES
			// lump might be interpreted as some UTF value. See https://github.com/jewalky/UltimateDoomBuilder/issues/827
			byte[] header = nodesreader.ReadBytes(4);
			if(unsupportedheaders.Where(e => Enumerable.SequenceEqual(e, header)).Any())
			{
				MessageBox.Show("ZDBSP compressed nodes are currently not supported.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				General.Editing.CancelMode();
				return false;
			}

			// Rewind stream position
			nodesreader.BaseStream.Position = 0;

			int numnodes = (int)nodesstream.Length / 28;

			//mxd. Boilerplate!
			if (numnodes < 1)
			{
				// Cancel mode
				MessageBox.Show("The map has only one subsector. Please add more sectors, then try running this mode again.", "THY NODETH ARETH BROKH!", MessageBoxButtons.OK, MessageBoxIcon.Error);
				General.Editing.CancelMode();
				return false;
			}

			
			nodes = new Node[numnodes];
			for(int i = 0; i < nodes.Length; i++)
			{
				nodes[i].linestart.x = nodesreader.ReadInt16();
				nodes[i].linestart.y = nodesreader.ReadInt16();
				nodes[i].linedelta.x = nodesreader.ReadInt16();
				nodes[i].linedelta.y = nodesreader.ReadInt16();
				float top = nodesreader.ReadInt16();
				float bot = nodesreader.ReadInt16();
				float left = nodesreader.ReadInt16();
				float right = nodesreader.ReadInt16();
				nodes[i].rightbox = new RectangleF(left, top, (right - left), (bot - top));
				top = nodesreader.ReadInt16();
				bot = nodesreader.ReadInt16();
				left = nodesreader.ReadInt16();
				right = nodesreader.ReadInt16();
				nodes[i].leftbox = new RectangleF(left, top, (right - left), (bot - top));
				int rightindex = nodesreader.ReadInt16();
				int leftindex = nodesreader.ReadInt16();
				nodes[i].rightchild = rightindex & 0x7FFF;
				nodes[i].leftchild = leftindex & 0x7FFF;
				nodes[i].rightsubsector = (rightindex & 0x8000) != 0;
				nodes[i].leftsubsector = (leftindex & 0x8000) != 0;
			}
			nodesreader.Close();

			// Add additional properties to nodes
			nodes[nodes.Length - 1].parent = -1;
			SetupNodes(); //mxd

			// Load the segs structure
			MemoryStream segsstream = General.Map.GetLumpData("SEGS");
			BinaryReader segsreader = new BinaryReader(segsstream);
			int numsegs = (int)segsstream.Length / 12;

			//mxd. Boilerplate!
			if(numsegs < 1) 
			{
				// Cancel mode
				MessageBox.Show("The map has empty SEGS lump. Please rebuild the nodes, then try running this mode again.", "THY SEGS HATH SINNETH!", MessageBoxButtons.OK, MessageBoxIcon.Error);
				General.Editing.CancelMode();
				return false;
			}

			//mxd. ZDoom SEGS overflow error
			if(numsegs >= ushort.MaxValue)
			{
				// Cancel mode
				MessageBox.Show("The map has too many SEGS (" + numsegs + "/" + ushort.MaxValue + ").\nIt won't load in Vanilla-style source ports\nand may not load in some enhanced source ports.", "THY SEGS ARETH WAAAY TOO PHAT!", MessageBoxButtons.OK, MessageBoxIcon.Error);
				General.Editing.CancelMode();
				return false;
			}
			//mxd. Vanilla SEGS overflow warning
			else if(numsegs >= short.MaxValue)
			{
				MessageBox.Show("The map has too many SEGS (" + numsegs + "/" + short.MaxValue + ").\nIt won't load in Vanilla-style source ports.", "THY SEGS ARETH TOO PHAT!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}

			segs = new Seg[numsegs];
			for(int i = 0; i < segs.Length; i++)
			{
				segs[i].startvertex = segsreader.ReadUInt16();
				segs[i].endvertex = segsreader.ReadUInt16();
				segs[i].angle = Angle2D.DegToRad(General.ClampAngle(segsreader.ReadInt16() / 182 + 90)); //mxd 182 == 65536 / 360; 
				segs[i].lineindex = segsreader.ReadUInt16();
				segs[i].leftside = segsreader.ReadInt16() != 0;
				segs[i].offset = segsreader.ReadInt16();
			}
			segsreader.Close();

			// Load the vertexes structure
			MemoryStream vertsstream = General.Map.GetLumpData("VERTEXES");
			BinaryReader vertsreader = new BinaryReader(vertsstream);
			int numverts = (int)vertsstream.Length / 4;

			//mxd. Boilerplate!
			if(numverts < 1) 
			{
				// Cancel mode
				MessageBox.Show("The map has empty VERTEXES lump. Please rebuild the nodes, then try running this mode again.", "THY VERTEXES ARETH FOUL!", MessageBoxButtons.OK, MessageBoxIcon.Error);
				General.Editing.CancelMode();
				return false;
			}

			verts = new Vector2D[numverts];
			for(int i = 0; i < verts.Length; i++)
			{
				verts[i].x = vertsreader.ReadInt16();
				verts[i].y = vertsreader.ReadInt16();
			}
			vertsreader.Close();

			// Load the subsectors structure
			MemoryStream ssecstream = General.Map.GetLumpData("SSECTORS");
			BinaryReader ssecreader = new BinaryReader(ssecstream);
			int numssec = (int)ssecstream.Length / 4;

			//mxd. Boilerplate!
			if(numssec < 1) 
			{
				// Cancel mode
				MessageBox.Show("The map has empty SSECTORS lump. Please rebuild the nodes, then try running this mode again.", "THY SSECTORS ARETH HERETYSH!", MessageBoxButtons.OK, MessageBoxIcon.Error);
				General.Editing.CancelMode();
				return false;
			}

			ssectors = new Subsector[numssec];
			for(int i = 0; i < ssectors.Length; i++)
			{
				ssectors[i].numsegs = ssecreader.ReadUInt16(); //TECH: these are short in Doom, ushort in ZDoom/PRBoom+
				ssectors[i].firstseg = ssecreader.ReadUInt16();
			}
			ssecreader.Close();

			// Link all segs to their subsectors
			for(int i = 0; i < ssectors.Length; i++)
			{
				int lastseg = ssectors[i].firstseg + ssectors[i].numsegs - 1;
				for(int sg = ssectors[i].firstseg; sg <= lastseg; sg++)
				{
					segs[sg].ssector = i;
				}
			}

			return true;
		}

		//mxd. This loads all data from the ZNODES lump
		private bool LoadZNodes() 
		{
			List<string> supportedformats = new List<string> { "XNOD", "XGLN", "XGL2", "XGL3" };
			MemoryStream stream = General.Map.GetLumpData("ZNODES");

			// Boilerplate...
			if(stream.Length < 4) 
			{
				MessageBox.Show("ZNODES lump is empty.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
				stream.Close();
				return false;
			}

			try
			{
				using(BinaryReader reader = new BinaryReader(stream))
				{
				// Read signature
				nodesformat = new string(reader.ReadChars(4));
				if(!supportedformats.Contains(nodesformat)) 
				{
					MessageBox.Show("\"" + nodesformat + "\" node format is not supported.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}

				uint vertscount = reader.ReadUInt32();
				uint newvertscount = reader.ReadUInt32();
				if(vertscount > int.MaxValue || newvertscount > int.MaxValue || (ulong)vertscount + newvertscount > int.MaxValue)
				{
					MessageBox.Show("Error while reading ZNODES: the vertex table is too large.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}

				// Boilerplate...
				if(vertscount != General.Map.Map.Vertices.Count) 
				{
					MessageBox.Show("Error while reading ZNODES: vertices count in ZNODES lump (" + vertscount + ") doesn't match with map's vertices count (" + General.Map.Map.Vertices.Count + ")!", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}

				// ZDBSP rewrites UDMF vertices into its own order, while ZNODES only
				// stores coordinates for the additional split vertices. Use the exact
				// original-vertex order captured from the builder output instead of the
				// editable MapSet array, whose order changes after a deletion.
				verts = new Vector2D[vertscount + newvertscount];
				Vector2D[] orderedverts = General.Map.GetNodebuilderVertexOrder((int)vertscount);
				if(General.Map.UDMF && orderedverts == null)
				{
					MessageBox.Show("Unable to match the ZNODES vertex order to the edited map. Rebuild the nodes and try again.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}

				int counter = 0;
				if(orderedverts != null)
				{
					Array.Copy(orderedverts, verts, orderedverts.Length);
					counter = orderedverts.Length;
				}
				else
				{
					foreach(Vertex v in General.Map.Map.Vertices) verts[counter++] = v.Position;
				}

				// Read extra vertices
				for(int i = counter; i < counter + newvertscount; i++) 
				{
					verts[i].x = reader.ReadInt32() / 65536.0f;
					verts[i].y = reader.ReadInt32() / 65536.0f;
				}

				// Read subsectors
				uint sseccount = reader.ReadUInt32();
				if(sseccount > int.MaxValue)
				{
					MessageBox.Show("Error while reading ZNODES: the subsector table is too large.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}
				ssectors = new Subsector[sseccount];

				int firstseg = 0;
				for(int i = 0; i < ssectors.Length; i++) 
				{
					uint numsegs = reader.ReadUInt32();
					if(numsegs == 0 || numsegs > int.MaxValue || numsegs > int.MaxValue - firstseg)
					{
						MessageBox.Show("Error while reading ZNODES: subsector " + i + " has an invalid segment range.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
						return false;
					}
					ssectors[i].numsegs = (int)numsegs;
					ssectors[i].firstseg = firstseg;
					firstseg += ssectors[i].numsegs;
				}

				// Read segments. Offset and angle are unused anyway
				uint segscount = reader.ReadUInt32();
				if(segscount > int.MaxValue || segscount != (uint)firstseg)
				{
					MessageBox.Show("Error while reading ZNODES: segment and subsector counts do not match.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}
				segs = new Seg[segscount];

				switch(nodesformat) 
				{
					case "XGLN":
						for(int i = 0; i < segs.Length; i++) 
						{
							segs[i].startvertex = (int)reader.ReadUInt32();
							reader.BaseStream.Position += 4; //skip partner
							ushort lineindex = reader.ReadUInt16();
							segs[i].lineindex = (lineindex == 0xFFFF ? -1 : lineindex);
							segs[i].leftside = reader.ReadBoolean();
						}
						break;

					case "XGL3":
					case "XGL2":
						for(int i = 0; i < segs.Length; i++) 
						{
							segs[i].startvertex = (int)reader.ReadUInt32();
							reader.BaseStream.Position += 4; //skip partner
							uint lineindex = reader.ReadUInt32();
							segs[i].lineindex = (lineindex == 0xFFFFFFFF ? -1 : (int)lineindex);
							segs[i].leftside = reader.ReadBoolean();
						}
						break;

					case "XNOD":
						for(int i = 0; i < segs.Length; i++) 
						{
							segs[i].startvertex = (int)reader.ReadUInt32();
							segs[i].endvertex = (int)reader.ReadUInt32();
							segs[i].lineindex = reader.ReadUInt16();
							segs[i].leftside = reader.ReadBoolean();
						}
						break;
				}

				// GL nodes omit the end vertex; it is the next segment's start.
				if(nodesformat == "XGLN" || nodesformat == "XGL2" || nodesformat == "XGL3")
				{
					foreach(Subsector ss in ssectors)
					{
						// Set the last vert
						int lastseg = ss.firstseg + ss.numsegs - 1;
						segs[lastseg].endvertex = segs[ss.firstseg].startvertex;

						// Set the rest
						for(int i = ss.firstseg + 1; i <= lastseg; i++) segs[i - 1].endvertex = segs[i].startvertex;

					}
				}

				// Validate all vertex references before the polygon builder sees them,
				// and fill properties shared by GL and non-GL extended nodes.
				for(int index = 0; index < ssectors.Length; index++)
				{
					Subsector ss = ssectors[index];
					int lastseg = ss.firstseg + ss.numsegs;
					for(int i = ss.firstseg; i < lastseg; i++)
					{
						if(segs[i].startvertex < 0 || segs[i].startvertex >= verts.Length || segs[i].endvertex < 0 || segs[i].endvertex >= verts.Length)
						{
							MessageBox.Show("Error while reading ZNODES: segment " + i + " refers to a missing vertex.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
							return false;
						}

						segs[i].angle = Vector2D.GetAngle(verts[segs[i].endvertex], verts[segs[i].startvertex]);
						segs[i].ssector = index;
					}
				}

				// Read nodes
				uint nodescount = reader.ReadUInt32();

				// Boilerplate...
				if(nodescount < 1)
				{
					MessageBox.Show("The map has only one subsector.\nPlease add more sectors before using this mode.", "Why are you doing this, Stanley?..", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}
				if(nodescount > int.MaxValue)
				{
					MessageBox.Show("Error while reading ZNODES: the node table is too large.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}

				nodes = new Node[nodescount];

				for(int i = 0; i < nodes.Length; i++) 
				{
					if(nodesformat == "XGL3") 
					{
						nodes[i].linestart.x = reader.ReadInt32() / 65536.0f;
						nodes[i].linestart.y = reader.ReadInt32() / 65536.0f;
						nodes[i].linedelta.x = reader.ReadInt32() / 65536.0f;
						nodes[i].linedelta.y = reader.ReadInt32() / 65536.0f;
					} 
					else 
					{
						nodes[i].linestart.x = reader.ReadInt16();
						nodes[i].linestart.y = reader.ReadInt16();
						nodes[i].linedelta.x = reader.ReadInt16();
						nodes[i].linedelta.y = reader.ReadInt16();
					}

					float top = reader.ReadInt16();
					float bot = reader.ReadInt16();
					float left = reader.ReadInt16();
					float right = reader.ReadInt16();
					nodes[i].rightbox = new RectangleF(left, top, (right - left), (bot - top));

					top = reader.ReadInt16();
					bot = reader.ReadInt16();
					left = reader.ReadInt16();
					right = reader.ReadInt16();
					nodes[i].leftbox = new RectangleF(left, top, (right - left), (bot - top));

					uint rightindex = reader.ReadUInt32();
					uint leftindex = reader.ReadUInt32();
					nodes[i].rightchild = (int)(rightindex & 0x7FFFFFFF);
					nodes[i].leftchild = (int)(leftindex & 0x7FFFFFFF);
					nodes[i].rightsubsector = (rightindex & 0x80000000) != 0;
					nodes[i].leftsubsector = (leftindex & 0x80000000) != 0;
				}

				// ZDBSP writes child nodes before their parents. Enforcing that order
				// catches corrupt/cyclic trees before they can recurse forever or index
				// outside the arrays. Every subsector must occur exactly once as a leaf.
				bool[] seenleaves = new bool[ssectors.Length];
				bool[] seennodes = new bool[nodes.Length];
				for(int i = 0; i < nodes.Length; i++)
				{
					Node n = nodes[i];
					if(!ValidateNodeChild(n.leftchild, n.leftsubsector, i, seenleaves, seennodes)
						|| !ValidateNodeChild(n.rightchild, n.rightsubsector, i, seenleaves, seennodes))
					{
						MessageBox.Show("Error while reading ZNODES: the BSP tree contains an invalid, duplicate, or cyclic child reference.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
						return false;
					}
				}

				for(int i = 0; i < seenleaves.Length; i++)
				{
					if(!seenleaves[i])
					{
						MessageBox.Show("Error while reading ZNODES: subsector " + i + " is not connected to the BSP tree.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
						return false;
					}
				}
				for(int i = 0; i < seennodes.Length - 1; i++)
				{
					if(!seennodes[i])
					{
						MessageBox.Show("Error while reading ZNODES: node " + i + " is not connected to the BSP tree.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
						return false;
					}
				}

				// Add additional properties to nodes
				nodes[nodes.Length - 1].parent = -1;
				SetupNodes(); //mxd
				}
			}
			catch(Exception e)
			{
				MessageBox.Show("Unable to read ZNODES safely:\n" + e.GetType().Name + ": " + e.Message, "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}

			return true;
		}

		private bool ValidateNodeChild(int child, bool subsector, int parent, bool[] seenleaves, bool[] seennodes)
		{
			if(subsector)
			{
				if(child < 0 || child >= seenleaves.Length || seenleaves[child]) return false;
				seenleaves[child] = true;
				return true;
			}

			if(child < 0 || child >= parent || seennodes[child]) return false;
			seennodes[child] = true;
			return true;
		}

		/// <summary>
		/// This recursively sets up the nodes structure with additional properties
		/// </summary>
		/*private void RecursiveSetupNodes(int nodeindex)
		{
			Node n = nodes[nodeindex];
			if(!n.leftsubsector)
			{
				nodes[n.leftchild].parent = nodeindex;
				RecursiveSetupNodes(n.leftchild);
			}
			if(!n.rightsubsector)
			{
				nodes[n.rightchild].parent = nodeindex;
				RecursiveSetupNodes(n.rightchild);
			}
		}*/

		/// <summary>
		/// This sets up the nodes structure with additional properties
		/// </summary>
		private void SetupNodes() //mxd. StackOverflowless implementation :)
		{
			for(int i = nodes.Length - 1; i > -1; i--)
			{
				Node n = nodes[i];
				if(!n.leftsubsector) nodes[n.leftchild].parent = i;
				if(!n.rightsubsector) nodes[n.rightchild].parent = i;
			}
		}

		/// <summary>
		/// This builds the polygons for all subsectors without recursing through
		/// the BSP tree. Pathological node trees can be thousands of levels deep;
		/// walking parent links keeps those maps from overflowing the UI thread's
		/// stack while reusing one split list for the whole build.
		/// </summary>
		private void BuildSubsectorPolygons()
		{
			int[] leafparents = new int[ssectors.Length];
			bool[] leafisleft = new bool[ssectors.Length];
			for(int i = 0; i < leafparents.Length; i++) leafparents[i] = -1;

			for(int i = 0; i < nodes.Length; i++)
			{
				Node node = nodes[i];
				if(node.leftsubsector)
				{
					leafparents[node.leftchild] = i;
					leafisleft[node.leftchild] = true;
				}
				if(node.rightsubsector)
				{
					leafparents[node.rightchild] = i;
					leafisleft[node.rightchild] = false;
				}
			}

			List<Split> splits = new List<Split>(64);
			for(int ss = 0; ss < ssectors.Length; ss++)
			{
				splits.Clear();
				int parent = leafparents[ss];
				int childnode = -1;
				bool isleft = leafisleft[ss];
				while(parent >= 0)
				{
					Node node = nodes[parent];
					splits.Add(new Split(node.linestart, isleft ? -node.linedelta : node.linedelta));
					childnode = parent;
					parent = node.parent;
					if(parent >= 0)
					{
						Node parentnode = nodes[parent];
						isleft = !parentnode.leftsubsector && parentnode.leftchild == childnode;
					}
				}

				BuildSubsectorPoly(ss, splits);
			}
		}

		/// <summary>
		/// Build the polygon for a specific subsector
		/// </summary>
		private void BuildSubsectorPoly(int ss, IEnumerable<Split> nodesplits)
		{
			// Start with the full coordinate range, as the original Nodes Viewer did.
			// The actual node path and every seg below trim this into the exact convex
			// subsector stored in the node lump.
			List<Vector2D> poly = new List<Vector2D>(16)
			{
				new Vector2D(-General.Map.FormatInterface.MaxCoordinate, General.Map.FormatInterface.MaxCoordinate),
				new Vector2D(General.Map.FormatInterface.MaxCoordinate, General.Map.FormatInterface.MaxCoordinate),
				new Vector2D(General.Map.FormatInterface.MaxCoordinate, -General.Map.FormatInterface.MaxCoordinate),
				new Vector2D(-General.Map.FormatInterface.MaxCoordinate, -General.Map.FormatInterface.MaxCoordinate)
			};

			// Crop the polygon by the node tree splits
			foreach(Split s in nodesplits) CropPolygon(poly, s);

			// Use the exact seg fragments from the generated node data. Replacing a
			// split seg with its complete source linedef, or dropping GL minisegs,
			// makes cells leak into their neighbours and produces the huge false
			// triangles that the viewer used to show around curves.
			for(int i = 0; i < ssectors[ss].numsegs; i++)
			{
				Split s;
				Seg sg = segs[ssectors[ss].firstseg + i];

				//mxd. Sanity check, because some segs in Doom maps refer to non-existing verts.  
				if(sg.startvertex > verts.Length - 1 || sg.endvertex > verts.Length - 1) continue;

				s.pos = verts[sg.startvertex];
				s.delta = verts[sg.endvertex] - verts[sg.startvertex];
				CropPolygon(poly, s);
			}

			if(poly.Count > 1)
			{
				// Remove any zero-length lines
				Vector2D prevpoint = poly[0];
				for(int i = poly.Count - 1; i >= 0; i--)
				{
					if(Vector2D.DistanceSq(poly[i], prevpoint) < 0.001f)
						poly.RemoveAt(i);
					else
						prevpoint = poly[i];
				}
			}

			ssectors[ss].points = poly.ToArray();

			// Setup vertices for rendering
			if(poly.Count >= 3)
			{
				FlatVertex[] fverts = new FlatVertex[(poly.Count - 2) * 3];
				int intcolor = PixelColor.FromColor(Color.Gray).WithAlpha(100).ToInt();
				int pi = 0;
				for(int t = 0; t < (poly.Count - 2); t++)
				{
					fverts[pi].x = (float)poly[0].x;
					fverts[pi].y = (float)poly[0].y;
					fverts[pi].c = intcolor;
					fverts[pi + 1].x = (float)poly[t + 1].x;
					fverts[pi + 1].y = (float)poly[t + 1].y;
					fverts[pi + 1].c = intcolor;
					fverts[pi + 2].x = (float)poly[t + 2].x;
					fverts[pi + 2].y = (float)poly[t + 2].y;
					fverts[pi + 2].c = intcolor;
					pi += 3;
				}
				ssectors[ss].vertices = fverts;
			}
		}

		// Combines the overview triangles into two immutable batches. The old
		// renderer issued one draw call (and, for colored mode, one allocation)
		// per subsector on every redraw. Large maps can contain thousands of
		// subsectors, so that made panning and hovering unnecessarily choppy.
		private void BuildOverviewGeometry()
		{
			long total = 0;
			for(int i = 0; i < ssectors.Length; i++)
			{
				if(ssectors[i].vertices != null) total += ssectors[i].vertices.Length;
			}

			if(total <= 0 || total > int.MaxValue)
			{
				overviewgrayvertices = new FlatVertex[0];
				overviewcoloredvertices = new FlatVertex[0];
				return;
			}

			overviewgrayvertices = new FlatVertex[(int)total];
			overviewcoloredvertices = new FlatVertex[(int)total];
			int offset = 0;
			for(int i = 0; i < ssectors.Length; i++)
			{
				FlatVertex[] source = ssectors[i].vertices;
				if(source == null || source.Length == 0) continue;

				Array.Copy(source, 0, overviewgrayvertices, offset, source.Length);
				Array.Copy(source, 0, overviewcoloredvertices, offset, source.Length);
				int color = distinctcolors[i % distinctcolors.Count].WithAlpha(100).ToInt();
				for(int v = offset; v < offset + source.Length; v++) overviewcoloredvertices[v].c = color;
				offset += source.Length;
			}
		}

		/// <summary>
		/// Crop a polygon by a split line
		/// </summary>
		private static void CropPolygon(List<Vector2D> poly, Split split)
		{
			if(poly.Count == 0) return;
			Vector2D prev = poly[poly.Count - 1];

			double side1 = (prev.y - split.pos.y) * split.delta.x - (prev.x - split.pos.x) * split.delta.y;
			List<Vector2D> newp = new List<Vector2D>(poly.Count);
			for(int i = 0; i < poly.Count; i++)
			{
				// Fetch vertex and determine side
				Vector2D cur = poly[i];
				double side2 = (cur.y - split.pos.y) * split.delta.x - (cur.x - split.pos.x) * split.delta.y;

				// Front?
				if(side2 < -EPSILON)
				{
					if(side1 > EPSILON)
					{
						// Split line with plane and insert the vertex
						Line2D.GetIntersection(split.pos, split.pos + split.delta, prev.x, prev.y, cur.x, cur.y, out double u, false);
						Vector2D newv = prev + (cur - prev) * u;
						newp.Add(newv);
					}

					newp.Add(cur);
				}
				// Back?
				else if(side2 > EPSILON)
				{
					if(side1 < -EPSILON)
					{
						// Split line with plane and insert the vertex
						Line2D.GetIntersection(split.pos, split.pos + split.delta, prev.x, prev.y, cur.x, cur.y, out double u, false);
						Vector2D newv = prev + (cur - prev) * u;
						newp.Add(newv);
					}
				}
				else
				{
					// On the plane
					newp.Add(cur);
				}

				// Next
				prev = cur;
				side1 = side2;
			}
			poly.Clear();
			poly.AddRange(newp);

			// The code below would be more efficient, because it modifies the polygon in place...
			// but it has a bug, some polygons are corrupted.

			/*
			bool prevremoved = false;
			float prevside = (prev.y - split.pos.y) * split.delta.x - (prev.x - split.pos.x) * split.delta.y;
			int i = 0;
			while(i < poly.Count)
			{
				Vector2D cur = poly[i];
				float curside = (cur.y - split.pos.y) * split.delta.x - (cur.x - split.pos.x) * split.delta.y;
				
				// Point is in FRONT of the split?
				if(curside < -EPSILON)
				{
					if(prevside > EPSILON)
					{
						// Previous point was BEHIND the split
						// Line crosses the split, we need to add the intersection point
						float u;
						Line2D.GetIntersection(split.pos, split.pos + split.delta, prev.x, prev.y, cur.x, cur.y, out u);
						Vector2D newv = prev + (cur - prev) * u;
						poly.Insert(i, newv);
						i++;
					}
					else if(prevside < -EPSILON)
					{
						// Previous point was also in FRONT of the split
						// We don't need to do anything
					}
					else
					{
						// Previous point was ON the split
						// If the previous point was removed, we have to add it again
						if(prevremoved)
						{
							poly.Insert(i, prev);
							i++;
						}
					}
					
					i++;
					prevremoved = false;
				}
				// Point is BEHIND the split?
				else if(curside > EPSILON)
				{
					if(prevside < -EPSILON)
					{
						// Previous point was in FRONT of the split
						// Line crosses the split, so we must add the intersection point
						float u;
						Line2D.GetIntersection(split.pos, split.pos + split.delta, prev.x, prev.y, cur.x, cur.y, out u);
						Vector2D newv = prev + (cur - prev) * u;
						poly.Insert(i, newv);
						i++;
					}
					else if(prevside > EPSILON)
					{
						// Previous point was also BEHIND the split
						// We don't need to do anything, this point will be removed
					}
					else
					{
						// Previous point was ON the split
						// We don't need to do anything, this point will be removed
					}
					
					poly.RemoveAt(i);
					prevremoved = true;
				}
				// Point is ON the split?
				else
				{
					if(prevside > EPSILON)
					{
						// Previous point was BEHIND the split
						// Remove this point
						poly.RemoveAt(i);
						prevremoved = true;
					}
					else if(prevside < -EPSILON)
					{
						// Previous point was in FRONT of the split
						// We want to keep this point
						prevremoved = false;
						i++;
					}
					else
					{
						// Previous point is ON the split
						// Only if the previous point was also removed, we remove this one as well
						if(prevremoved)
							poly.RemoveAt(i);
						else
							i++;
					}
				}
				
				prev = cur;
				prevside = curside;
			}
			*/
		}

		/// <summary>
		/// This tests if the given coordinate is inside the specified subsector.
		/// </summary>
		private bool PointInSubsector(int index, Vector2D p) 
		{
			if(ssectors[index].points.Length == 0) return false; //mxd
			
			// Subsectors are convex, so we can simply test if the point is on the front side of all lines.
			Vector2D[] points = ssectors[index].points;
			Vector2D prevpoint = points[points.Length - 1];
			for(int i = 0; i < points.Length; i++)
			{
				double side = Line2D.GetSideOfLine(prevpoint, points[i], p);
				if(side > 0f) return false;
				prevpoint = points[i];
			}
			return true;
		}

		// For rendering
		private void DrawSubsectorArea(FlatVertex[] vertices, PixelColor color)
		{
			if(vertices == null) return;
			if(vertices.Length < 3) return;

			// Copy array and change color
			FlatVertex[] poly = new FlatVertex[vertices.Length];
			vertices.CopyTo(poly, 0);
			int intcolor = color.WithAlpha(100).ToInt();
			for(int i = 0; i < poly.Length; i++) poly[i].c = intcolor;

			// Draw
			renderer.RenderGeometry(poly, null, true);
		}

		// For rendering
		private void DrawSubsectorArea(FlatVertex[] vertices)
		{
			if(vertices == null || vertices.Length < 3) return;
			renderer.RenderGeometry(vertices, null, true);
		}

		// For rendering
		private void PlotSubsectorLines(Vector2D[] points, PixelColor color)
		{
			if(points.Length < 2) return;
			Vector2D prevpoint = points[points.Length - 1];
			for(int i = 0; i < points.Length; i++)
			{
				renderer.PlotLine(prevpoint, points[i], color);
				prevpoint = points[i];
			}
		}

		// For rendering
		private void DrawSplitArea(RectangleF bbox, int nodeindex, bool left, PixelColor color)
		{
			Node node = nodes[nodeindex];

			// Begin with a square bounding box polygon
			List<Vector2D> poly = new List<Vector2D>(16)
			{
				new Vector2D(bbox.Left, bbox.Top),
				new Vector2D(bbox.Right, bbox.Top),
				new Vector2D(bbox.Right, bbox.Bottom),
				new Vector2D(bbox.Left, bbox.Bottom)
			};

			// Remove everything behind the split from the area
			if(left)
				CropPolygon(poly, new Split(node.linestart, -node.linedelta));
			else
				CropPolygon(poly, new Split(node.linestart, node.linedelta));

			// Remove everything behind parent splits from the area
			int prevnode = nodeindex;
			int parentnode = node.parent;
			while(parentnode > -1)
			{
				Node pn = nodes[parentnode];
				if(!pn.leftsubsector && (pn.leftchild == prevnode))
					CropPolygon(poly, new Split(pn.linestart, -pn.linedelta));
				else if(!pn.rightsubsector && (pn.rightchild == prevnode))
					CropPolygon(poly, new Split(pn.linestart, pn.linedelta));
				prevnode = parentnode;
				parentnode = pn.parent;
			}

			if(poly.Count >= 3)
			{
				// Create render vertices
				FlatVertex[] fverts = new FlatVertex[(poly.Count - 2) * 3];
				int intcolor = color.ToInt();
				int pi = 0;
				for(int t = 0; t < (poly.Count - 2); t++)
				{
					fverts[pi].x = (float)poly[0].x;
					fverts[pi].y = (float)poly[0].y;
					fverts[pi].c = intcolor;
					fverts[pi + 1].x = (float)poly[t + 1].x;
					fverts[pi + 1].y = (float)poly[t + 1].y;
					fverts[pi + 1].c = intcolor;
					fverts[pi + 2].x = (float)poly[t + 2].x;
					fverts[pi + 2].y = (float)poly[t + 2].y;
					fverts[pi + 2].c = intcolor;
					pi += 3;
				}

				// Draw
				renderer.RenderGeometry(fverts, null, true);
			}
		}

		#endregion

		#region ================== Events

		// Mode starts
		public override void OnEngage()
		{
			Cursor.Current = Cursors.WaitCursor;
			base.OnEngage();

			if(General.Map.Map.Vertices.Count == 0)
			{
				General.ToastManager.ShowToast(ToastMessages.NODESVIEWER, ToastType.ERROR, "Failed to engage Nodes Viewer Mode", "The map is empty.", "Failed to engage Nodes Viewer Mode: the map is empty");
				General.Editing.CancelMode();
				return;
			}

			//mxd
			bool haveNodes = General.Map.LumpExists("NODES");
			bool haveZnodes = General.Map.LumpExists("ZNODES");
			bool haveSectors = General.Map.LumpExists("SSECTORS");
			bool haveSegs = General.Map.LumpExists("SEGS");
			bool haveVerts = General.Map.LumpExists("VERTEXES");
			bool haveClassicNodes = haveNodes && haveSectors && haveSegs && haveVerts;
			bool haveCurrentVertexOrder = General.Map.HasNodebuilderVertexOrder(General.Map.Map.Vertices.Count);
			bool needUdmfVertexOrder = haveZnodes && General.Map.UDMF && !haveCurrentVertexOrder;
			bool nodesAreOutOfDate = General.Map.IsChanged && !haveCurrentVertexOrder;

			if(nodesAreOutOfDate || (!haveZnodes && !haveClassicNodes) || needUdmfVertexOrder)
			{
				// We need to build the nodes!
				if (!General.Map.RebuildNodes(General.Map.ConfigSettings.NodebuilderSave, true))
				{
					General.ToastManager.ShowToast(ToastMessages.NODESVIEWER, ToastType.ERROR, "Failed to engage Nodes Viewer Mode", "Failed to rebuild the nodes.", "Failed to engage Nodes Viewer Mode: failed to rebuild the nodes");
					General.Editing.CancelMode();
					return;
				}

				//mxd. Update nodes availability
				haveNodes = General.Map.LumpExists("NODES");
				haveZnodes = General.Map.LumpExists("ZNODES");
				haveSectors = General.Map.LumpExists("SSECTORS");
				haveSegs = General.Map.LumpExists("SEGS");
				haveVerts = General.Map.LumpExists("VERTEXES");
			}

			//mxd
			if(haveZnodes)
			{
				// ZDBSP may reorder original UDMF vertices in the TEXTMAP it writes.
				// LoadZNodes pairs the node lump with the exact order captured from that
				// build, preventing deletions from scrambling the displayed subsectors.
				General.Interface.DisplayStatus(StatusType.Busy, "Reading map nodes...");
				if(!LoadZNodes())
				{
					General.Interface.DisplayStatus(StatusType.Warning, "Failed to read map nodes.");
					General.Editing.CancelMode();
					return;
				}
			}
			else 
			{
				if(!haveNodes) 
				{
					MessageBox.Show("Unable to find the NODES lump. It may be that the nodes could not be built correctly.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
					General.Editing.CancelMode();
					return;
				}

				if(!haveSectors) 
				{
					MessageBox.Show("Unable to find the SSECTORS lump. It may be that the nodes could not be built correctly.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
					General.Editing.CancelMode();
					return;
				}

				if(!haveSegs) 
				{
					MessageBox.Show("Unable to find the SEGS lump. It may be that the nodes could not be built correctly.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
					General.Editing.CancelMode();
					return;
				}

				if(!haveVerts) 
				{
					MessageBox.Show("Unable to find the VERTEXES lump. It may be that the nodes could not be built correctly.", "Nodes Viewer mode", MessageBoxButtons.OK, MessageBoxIcon.Error);
					General.Editing.CancelMode();
					return;
				}

				General.Interface.DisplayStatus(StatusType.Busy, "Reading map nodes...");
				if(!LoadClassicStructures())
				{
					General.Interface.DisplayStatus(StatusType.Warning, "Failed to read map nodes.");
					General.Editing.CancelMode();
					return;
				}
			}
			
			// Setup presentation
			CustomPresentation presentation = new CustomPresentation();
			presentation.AddLayer(new PresentLayer(RendererLayer.Background, BlendingMode.Mask, General.Settings.BackgroundAlpha));
			presentation.AddLayer(new PresentLayer(RendererLayer.Grid, BlendingMode.Mask));
			presentation.AddLayer(new PresentLayer(RendererLayer.Overlay, BlendingMode.Alpha, 1f, true));
			presentation.AddLayer(new PresentLayer(RendererLayer.Geometry, BlendingMode.Alpha, 1f, true));
			renderer.SetPresentation(presentation);

			General.Interface.DisplayStatus(StatusType.Busy, "Building subsectors...");

			Stopwatch polygonwatch = Stopwatch.StartNew();
			BuildSubsectorPolygons();
			BuildOverviewGeometry();
			int emptysubsectors = 0;
			for(int i = 0; i < ssectors.Length; i++)
			{
				if(ssectors[i].vertices == null || ssectors[i].vertices.Length < 3) emptysubsectors++;
			}
			General.WriteLogLine("Nodes Viewer polygon build: " + ssectors.Length + " subsectors, "
				+ emptysubsectors + " empty, " + (overviewgrayvertices.Length / 3) + " batched triangles in "
				+ polygonwatch.ElapsedMilliseconds + " ms.");

			// Load and display dialog window
			form = new NodesForm(this);
			form.Text += " (" + nodesformat + " format)";
			form.Show((Form)General.Interface);

			Cursor.Current = Cursors.Default;
			General.Interface.DisplayReady();
			General.Interface.RedrawDisplay();
		}

		// Mode ends
		public override void OnDisengage()
		{
			if(form != null)
			{
				form.Dispose();
				form = null;
			}
			
			base.OnDisengage();
		}

		// Cancelled
		public override void OnCancel()
		{
			// Cancel base class
			base.OnCancel();

			// Return to the previous classic mode, or a safe available 2D fallback
			// when this volatile viewer was restored directly at map startup.
			General.Editing.ReturnToPreviousClassicMode();
		}

		// Mouse moves
		public override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if(form == null || nodes == null || nodes.Length == 0) return;

			if(form.SelectedTab == 0)
			{
				int newssector = -1;

				// Traverse the tree to find the subsector we could be in
				Node n = nodes[nodes.Length - 1];
				do
				{
					double side = (mousemappos.y - n.linestart.y) * n.linedelta.x - (mousemappos.x - n.linestart.x) * n.linedelta.y;
					if(side > 0f)
					{
						// Mouse is on the left side of this split
						if(n.leftsubsector)
							newssector = n.leftchild;
						else
							n = nodes[n.leftchild];
					}
					else
					{
						// Mouse is on the right side of this split
						if(n.rightsubsector)
							newssector = n.rightchild;
						else
							n = nodes[n.rightchild];
					}
				}
				while(newssector == -1);

				// The mouse could be outside the map (which can't be determined through the BSP tree).
				// So here we check if the mouse is really inside the subsector.
				if((newssector > -1) && !PointInSubsector(newssector, mousemappos)) newssector = -1;
				
				// Update?
				if(newssector != mouseinssector)
				{
					mouseinssector = newssector;
					General.Interface.RedrawDisplay();
				}
			}
		}

		// Mouse leaves
		public override void OnMouseLeave(EventArgs e)
		{
			base.OnMouseLeave(e);
			mouseinssector = -1;
			General.Interface.RedrawDisplay();
		}

		// Mouse clicks to select a subsector
		protected override void OnSelectBegin()
		{
			base.OnSelectBegin();
			if(mouseinssector > -1)
			{
				form.ShowSubsector(mouseinssector);
			}
		}

		// Draw the display
		public override void OnRedrawDisplay()
		{
			renderer.RedrawSurface();

			if(form == null) return;

			if(renderer.StartPlotter(true))
			{
				if(form.SelectedTab == 0)
				{
					// Render all subsectors in original color
					for(int si = 0; si < ssectors.Length; si++) 
					{
						Subsector s = ssectors[si];
						PlotSubsectorLines(s.points, PixelColor.FromColor(Color.Gray));
					}
					
					if(mouseinssector > -1)
					{
						PlotSubsectorLines(ssectors[mouseinssector].points, General.Colors.Highlight);
					}

					// Draw additional vertices
					if(form.ShowSegsVertices)
					{
						for(int i = General.Map.Map.Vertices.Count; i < verts.Length; i++)
							renderer.PlotVertexAt(verts[i], ColorCollection.VERTICES);
					}
				}

				if(form.SelectedTab == 1)
				{
					renderer.PlotLinedefSet(General.Map.Map.Linedefs);

					// Render selected node split
					if((form.ViewSplitIndex >= 0) && (form.ViewSplitIndex < nodes.Length))
					{
						Node n = nodes[form.ViewSplitIndex];

						// Draw parent splits
						int parentsplit = n.parent;
						while(parentsplit > -1)
						{
							Node pn = nodes[parentsplit];
							renderer.PlotLine(pn.linestart, pn.linestart + pn.linedelta, General.Colors.Selection);
							parentsplit = pn.parent;
						}

						// Draw this split
						renderer.PlotLine(n.linestart, n.linestart + n.linedelta, General.Colors.Highlight);
					}
				}

				if(form.SelectedTab == 2)
				{
					renderer.PlotLinedefSet(General.Map.Map.Linedefs);

					// Render selected subsector
					if((form.ViewSubsectorIndex >= 0) && (form.ViewSubsectorIndex < ssectors.Length))
					{
						Subsector s = ssectors[form.ViewSubsectorIndex];
						PlotSubsectorLines(s.points, General.Colors.Highlight);
					}

					// Draw selected segment
					if(form.ViewSegIndex > -1)
					{
						Seg sg = segs[form.ViewSegIndex];
						renderer.PlotLine(verts[sg.startvertex], verts[sg.endvertex], General.Colors.Selection);
					}
				}

				renderer.Finish();
			}

			if(renderer.StartOverlay(true))
			{
				switch(form.SelectedTab)
				{
					case 0:
						if(mouseinssector > -1)
						{
							// Render every subsector in one cached draw call.
							DrawSubsectorArea(overviewgrayvertices);
							DrawSubsectorArea(ssectors[mouseinssector].vertices, General.Colors.Highlight);
						}
						else
						{
							// Distinct colors are precomputed once when the mode is engaged.
							DrawSubsectorArea(overviewcoloredvertices);
						}
						break;

					case 1:
						if((form.ViewSplitIndex >= 0) && (form.ViewSplitIndex < nodes.Length))
						{
							Node n = nodes[form.ViewSplitIndex];

							// Draw areas. We draw these first, because they would otherwise erase any splits we want to show.
							DrawSplitArea(n.leftbox, form.ViewSplitIndex, true, new PixelColor(100, 50, 80, 255));
							DrawSplitArea(n.rightbox, form.ViewSplitIndex, false, new PixelColor(100, 20, 220, 20));

							// Draw parent splits
							int parentsplit = n.parent;
							while(parentsplit > -1)
							{
								Node pn = nodes[parentsplit];
								renderer.RenderLine(pn.linestart, pn.linestart + pn.linedelta, 1f, General.Colors.Selection, true);
								parentsplit = pn.parent;
							}

							// Draw this split
							renderer.RenderLine(n.linestart, n.linestart + n.linedelta, 1f, General.Colors.Highlight, true);
						}
						break;

					case 2:
						if((form.ViewSubsectorIndex >= 0) && (form.ViewSubsectorIndex < ssectors.Length))
						{
							Subsector s = ssectors[form.ViewSubsectorIndex];

							// Draw area
							DrawSubsectorArea(s.vertices, General.Colors.Highlight);

							// Draw selected segment
							if(form.ViewSegIndex > -1)
							{
								Seg sg = segs[form.ViewSegIndex];
								renderer.RenderLine(verts[sg.startvertex], verts[sg.endvertex], 1f, General.Colors.Selection, true);
							}
						}
						break;
				}
				
				renderer.Finish();
			}
			
			renderer.Present();
		}

		#endregion
	}
}
