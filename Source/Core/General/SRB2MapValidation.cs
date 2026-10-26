using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Map;

namespace CodeImp.DoomBuilder
{
	/// <summary>
	/// Validation helpers for SRB2 map features that otherwise fail while the
	/// engine is starting the level.
	/// </summary>
	public static class SRB2MapValidation
	{
		private const string VERTEX_SLOPE_ACTION_ID = "srb2_vertexslope";
		private const int SLOPE_VERTEX_THING_TYPE = 750;

		public static bool IsVertexSlopeLinedef(Linedef line)
		{
			if(line == null || line.IsDisposed || General.Map == null || General.Map.Config == null)
				return false;

			LinedefActionInfo info;
			return General.Map.Config.LinedefActions.TryGetValue(line.Action, out info)
				&& string.Equals(info.Id, VERTEX_SLOPE_ACTION_ID, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Returns the zero-based vertex positions that SRB2 will be unable to
		/// resolve for a Create Vertex-Based Slope linedef. SRB2 assigns the
		/// matching things in map order, so repeated tags require multiple things.
		/// </summary>
		public static List<int> GetMissingSlopeVertexSlots(Linedef line)
		{
			List<int> missing = new List<int>();
			if(!IsVertexSlopeLinedef(line)) return missing;

			Thing[] vertices = new Thing[3];
			int[] tags = { line.Args[1], line.Args[2], line.Args[3] };

			foreach(Thing thing in General.Map.Map.Things)
			{
				if(thing == null || thing.IsDisposed || thing.Type != SLOPE_VERTEX_THING_TYPE) continue;

				if(vertices[0] == null && thing.Tag == tags[0])
					vertices[0] = thing;
				else if(vertices[1] == null && thing.Tag == tags[1])
					vertices[1] = thing;
				else if(vertices[2] == null && thing.Tag == tags[2])
					vertices[2] = thing;
			}

			for(int i = 0; i < vertices.Length; i++)
				if(vertices[i] == null) missing.Add(i);

			return missing;
		}
	}
}
