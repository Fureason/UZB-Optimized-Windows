
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Config;

#endregion

namespace CodeImp.DoomBuilder.Controls
{
	public partial class ActionSelectorControl : UserControl
	{
		// Events
		public event EventHandler ValueChanges;
		
		// Variables
		private List<GeneralizedCategory> generalizedcategories;
		private List<GeneralizedOption> generalizedoptions; //mxd
		private bool controlpressed;
		private readonly Dictionary<int, int> itemindices;
		private readonly Timer valuechangetimer;
		
		// Constants
		private const string NUMBER_SEPERATOR = "\t";
		
		// Properties
		public bool Empty { get { return (number.Text.Length == 0); } set { if(value) number.Text = ""; } }
		public int Value { get { return GetValue(); } set { number.Text = value.ToString(); } }
		public List<GeneralizedCategory> GeneralizedCategories { get { return generalizedcategories; } set { generalizedcategories = value; } }
		public List<GeneralizedOption> GeneralizedOptions { get { return generalizedoptions; } set { generalizedoptions = value; } } //mxd
		
		// Constructor
		public ActionSelectorControl()
		{
			// Initialize
			InitializeComponent();
			itemindices = new Dictionary<int, int>();
			valuechangetimer = new Timer { Interval = 120 };
			valuechangetimer.Tick += valuechangetimer_Tick;
			number.Leave += number_Leave;
			Disposed += ActionSelectorControl_Disposed;
			number.MouseWheel += number_OnMouseWheel; //mxd
		}

		// This returns the numeric value
		public int GetValue()
		{
			int val = 0;

			if(number.Text.Length > 0)
			{
				try
				{
					val = Convert.ToInt32(number.Text);
				}
				catch(Exception) { }
			}
			
			return val;
		}

		// This clears all information
		public void ClearInfo()
		{
			list.Items.Clear();
			itemindices.Clear();
		}
		
		// This adds information to display
		public void AddInfo(INumberedTitle[] infolist)
		{
			// Add to list
			int firstindex = list.Items.Count;
			list.Items.AddRange(infolist);
			for(int i = 0; i < infolist.Length; i++)
				if(!itemindices.ContainsKey(infolist[i].Index))
					itemindices.Add(infolist[i].Index, firstindex + i);
		}

		// Resized
		private void ActionSelectorControl_Resize(object sender, EventArgs e)
		{
			list.Width = ClientRectangle.Width - list.Left;
			ClientSize = new Size(ClientSize.Width, list.Height);
		}

		// Layout change
		private void ActionSelectorControl_Layout(object sender, LayoutEventArgs e)
		{
			ActionSelectorControl_Resize(sender, e);
		}

		// This draws an item in the combobox
		private void list_DrawItem(object sender, DrawItemEventArgs e)
		{
			Brush displaybrush = SystemBrushes.WindowText;
			Brush backbrush = SystemBrushes.Window;
			string displayname = string.Empty;

			// Only when running
			if(!this.DesignMode)
			{
				// Unknow item?
				if(e.Index < 0)
				{
					// Grayed
					displaybrush = new SolidBrush(SystemColors.GrayText);
					backbrush = new SolidBrush(SystemColors.Window);

					// Try getting integral number
					int intnumber;
					int.TryParse(number.Text, out intnumber);

					// Check what to display
					if(number.Text.Length == 0)
						displayname = "";
					else if(intnumber == 0)
						displayname = "None";
					else if((generalizedcategories != null) && GameConfiguration.IsGeneralized(intnumber, generalizedcategories))
						displayname = "Generalized (" + General.Map.Config.GetGeneralizedActionCategory(intnumber) + ")";
					else if((generalizedoptions != null) && GameConfiguration.IsGeneralizedSectorEffect(intnumber, generalizedoptions)) //mxd
						displayname = General.Map.Config.GetGeneralizedSectorEffectName(intnumber); //mxd
					else
						displayname = "Unknown";
				}
				// In the display part of the combobox?
				else if((e.State & DrawItemState.ComboBoxEdit) != 0)
				{
					// Show without number
					INumberedTitle item = (INumberedTitle)list.Items[e.Index];
					displayname = item.Title.Trim();

					// Determine colors to use
					if(item.Index == 0)
					{
						// Grayed
						displaybrush = new SolidBrush(SystemColors.GrayText);
						backbrush = new SolidBrush(SystemColors.Window);
					}
					else
					{
						// Normal color
						displaybrush = new SolidBrush(list.ForeColor);
						backbrush = new SolidBrush(SystemColors.Window);
					}
				}
				else
				{
					// Use number and description
					INumberedTitle item = (INumberedTitle)list.Items[e.Index];
					displayname = item.Index + NUMBER_SEPERATOR + item.Title;

					// Determine colors to use
					if((e.State & DrawItemState.Focus) != 0)
					{
						displaybrush = new SolidBrush(SystemColors.HighlightText);
						backbrush = new SolidBrush(SystemColors.Highlight);
					}
					else
					{
						displaybrush = new SolidBrush(list.ForeColor);
						backbrush = new SolidBrush(SystemColors.Window);
					}
				}
			}

			// Draw item
			e.Graphics.FillRectangle(backbrush, e.Bounds);
			e.Graphics.DrawString(displayname, list.Font, displaybrush, e.Bounds.X, e.Bounds.Y);

			//mxd. Dispose brushes
			if(!this.DesignMode)
			{
				backbrush.Dispose();
				displaybrush.Dispose();
			}
		}

		// List closed
		private void list_DropDownClosed(object sender, EventArgs e)
		{
			// Focus to number box
			number.SelectAll();
			number.Focus();
		}

		// Number changes
		private void number_TextChanged(object sender, EventArgs e)
		{
			int itemindex = -1;

			// Not nothing?
			if(number.Text.Length > 0)
			{
				int enteredvalue;
				if(int.TryParse(number.Text, out enteredvalue))
				{
					int foundindex;
					if(itemindices.TryGetValue(enteredvalue, out foundindex)) itemindex = foundindex;
				}
			}

			// Select item
			if(list.SelectedIndex != itemindex)
			{
				list.SelectedIndex = itemindex;
				list.Refresh();
			}
			//mxd. This may be generalized effect, and it may've changed
			else if(itemindex == -1)
			{
				list.Refresh();
			}

			// Updating all action argument controls for every intermediate digit makes
			// values such as 100 visibly crawl through 1 and 10. Programmatic changes
			// and list selections remain immediate; keyboard entry is coalesced.
			// This still fires immediately during edit-form setup, which TypeHandlers require.
			if(number.Focused)
			{
				valuechangetimer.Stop();
				valuechangetimer.Start();
			}
			else
			{
				RaiseValueChanges();
			}
		}

		private void valuechangetimer_Tick(object sender, EventArgs e)
		{
			valuechangetimer.Stop();
			RaiseValueChanges();
		}

		private void number_Leave(object sender, EventArgs e)
		{
			if(!valuechangetimer.Enabled) return;
			valuechangetimer.Stop();
			RaiseValueChanges();
		}

		private void RaiseValueChanges()
		{
			if(ValueChanges != null) ValueChanges(this, EventArgs.Empty);
		}

		private void ActionSelectorControl_Disposed(object sender, EventArgs e)
		{
			valuechangetimer.Dispose();
		}

		// Keys pressed in number box
		private void number_KeyDown(object sender, KeyEventArgs e)
		{
			controlpressed = e.Control;

			// Allow CTRL+X, CTRL+C and CTRL+V
			if(controlpressed && ((e.KeyCode == Keys.X) || (e.KeyCode == Keys.C) || (e.KeyCode == Keys.V))) return;

			//mxd. Scroll action list using arrow keys
			if(e.KeyCode == Keys.Down)
			{
				if(list.SelectedIndex > 0)
				{
					list.SelectedIndex--;
					list_SelectionChangeCommitted(list, EventArgs.Empty);
				}
				// Cancel this
				e.Handled = true;
				return;
			}
			if(e.KeyCode == Keys.Up) 
			{
				if(list.SelectedIndex < list.Items.Count - 1)
				{
					list.SelectedIndex++;
					list_SelectionChangeCommitted(list, EventArgs.Empty);
				}
				// Cancel this
				e.Handled = true;
				return;
			}

			// Not numeric or control key?
			if(((e.KeyValue < 48) || (e.KeyValue > 57)) &&
			   (e.KeyCode != Keys.Back) && (e.KeyCode != Keys.Left) &&
			   (e.KeyCode != Keys.Right) && (e.KeyCode != Keys.Delete))
			{
				// Cancel this
				e.Handled = true;
			}
		}

		// Keys pressed in number box
		private void number_KeyPress(object sender, KeyPressEventArgs e)
		{
			// Allow CTRL+X, CTRL+C and CTRL+V
			if(controlpressed && ((e.KeyChar == '\u0018') || (e.KeyChar == '\u0003') || (e.KeyChar == '\u0016'))) return;
			
			// Not numeric or control key?
			if(((e.KeyChar < 48) || (e.KeyChar > 57)) && (e.KeyChar != 8))
			{
				// Cancel this
				e.Handled = true;
			}
		}

		//mxd. Scrolls action list using mouse wheel
		private void number_OnMouseWheel(object sender, MouseEventArgs e) 
		{
			if(e.Delta < 0 && list.SelectedIndex > 0)
			{
				list.SelectedIndex--;
				list_SelectionChangeCommitted(list, EventArgs.Empty);
			}
			else if(e.Delta > 0 && list.SelectedIndex < list.Items.Count - 1)
			{
				list.SelectedIndex++;
				list_SelectionChangeCommitted(list, EventArgs.Empty);
			}
		}
		
		// Selection made
		private void list_SelectionChangeCommitted(object sender, EventArgs e)
		{
			INumberedTitle item = (INumberedTitle)list.SelectedItem;
			number.Text = item.Index.ToString();
		}
	}
}
