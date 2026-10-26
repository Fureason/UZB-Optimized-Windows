#region ================== Namespaces

using CodeImp.DoomBuilder.Map;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	public interface ILinedefEditForm
	{
		// The edited linedefs are exposed so Visual Mode can refresh only geometry
		// which depends on a changed control linedef.
		ICollection<Linedef> EditedLinedefs { get; }
		bool InvisibleFOFPreviewAlphaChanged { get; }

		//Events
		event EventHandler OnValuesChanged;
		event EventHandler Closed;

		// Methods
		void Setup(ICollection<Linedef> lines, bool selectfront, bool selectback);
		DialogResult ShowDialog(IWin32Window owner);
		void Dispose();
	}
}
