/// <reference path="../udbscript.d.ts" />

`#version 5`;

`#name Optimize map (safe preview)`;

`#description Analyzes obviously redundant geometry, shows exactly what it found, and only changes the map after confirmation. Script changes can be undone normally.`;

`#scriptoptions

removezerolength
{
	description = "Remove zero-length linedefs";
	default = true;
	type = 3;
}

removeorphanvertices
{
	description = "Remove vertices with no linedefs";
	default = true;
	type = 3;
}

removeorphansectors
{
	description = "Remove sectors with no sidedefs (cannot appear in the map)";
	default = true;
	type = 3;
}

mergeduplicatevertices
{
	description = "Merge vertices at identical coordinates (may join intentionally overlapping geometry)";
	default = false;
	type = 3;
}
`;

function getDuplicateVertexGroups()
{
	let positions = {};
	let groups = [];

	UDB.Map.getVertices().forEach(vertex => {
		let key = vertex.position.x.toString() + "," + vertex.position.y.toString();
		if(positions[key] === undefined)
		{
			positions[key] = [vertex];
		}
		else
		{
			positions[key].push(vertex);
		}
	});

	Object.keys(positions).forEach(key => {
		if(positions[key].length > 1) groups.push(positions[key]);
	});

	return groups;
}

function getZeroLengthLinedefs()
{
	return UDB.Map.getLinedefs().filter(line => line.lengthSq <= 0.0000000001);
}

function getOrphanVertices()
{
	return UDB.Map.getVertices().filter(vertex => vertex.getLinedefs().length === 0);
}

function getOrphanSectors()
{
	return UDB.Map.getSectors().filter(sector => sector.getSidedefs().length === 0);
}

let zeroLength = getZeroLengthLinedefs();
let orphanVertices = getOrphanVertices();
let orphanSectors = getOrphanSectors();
let duplicateGroups = getDuplicateVertexGroups();
let duplicateVertices = duplicateGroups.reduce((count, group) => count + group.length - 1, 0);

let report = "Map optimization analysis:\n\n"
	+ zeroLength.length + " zero-length linedef(s)\n"
	+ orphanVertices.length + " unreferenced vertex/vertices\n"
	+ orphanSectors.length + " unreferenced sector(s) with no sidedefs\n"
	+ duplicateVertices + " duplicate vertex/vertices at identical coordinates\n\n"
	+ "Selected fixes:\n"
	+ (UDB.ScriptOptions.removezerolength ? "• Remove zero-length linedefs\n" : "")
	+ (UDB.ScriptOptions.removeorphanvertices ? "• Remove unreferenced vertices\n" : "")
	+ (UDB.ScriptOptions.removeorphansectors ? "• Remove sectors that have no sidedefs\n" : "")
	+ (UDB.ScriptOptions.mergeduplicatevertices ? "• Merge identical-coordinate vertices\n" : "")
	+ "\nApply the selected fixes? You can Undo the complete operation.";

let selectedChanges = (UDB.ScriptOptions.removezerolength && zeroLength.length > 0)
	|| (UDB.ScriptOptions.removeorphanvertices && orphanVertices.length > 0)
	|| (UDB.ScriptOptions.removeorphansectors && orphanSectors.length > 0)
	|| (UDB.ScriptOptions.mergeduplicatevertices && duplicateVertices > 0);

if(!selectedChanges)
{
	UDB.showMessage(report.replace("\n\nApply the selected fixes? You can Undo the complete operation.",
		"\n\nNo selected fix has anything to change."));
}
else if(UDB.showMessageYesNo(report))
{
	let removedLines = 0;
	let removedVertices = 0;
	let removedSectors = 0;
	let mergedVertices = 0;

	if(UDB.ScriptOptions.removezerolength)
	{
		zeroLength.forEach(line => {
			line.delete();
			removedLines++;
		});
	}

	if(UDB.ScriptOptions.mergeduplicatevertices)
	{
		// Recalculate after deleting degenerate lines so every wrapper is current.
		getDuplicateVertexGroups().forEach(group => {
			let keeper = group[0];
			for(let i = 1; i < group.length; i++)
			{
				group[i].join(keeper);
				mergedVertices++;
			}
		});

		// Joining coincident vertices can expose additional degenerate lines.
		if(UDB.ScriptOptions.removezerolength)
		{
			getZeroLengthLinedefs().forEach(line => {
				line.delete();
				removedLines++;
			});
		}
	}

	if(UDB.ScriptOptions.removeorphanvertices)
	{
		getOrphanVertices().forEach(vertex => {
			vertex.delete();
			removedVertices++;
		});
	}

	if(UDB.ScriptOptions.removeorphansectors)
	{
		// Recalculate after line cleanup. A zero-length line can be the final stale
		// reference keeping an otherwise unreachable sector in the map structure.
		getOrphanSectors().forEach(sector => {
			sector.delete();
			removedSectors++;
		});
	}

	UDB.showMessage("Map optimization complete:\n\n"
		+ removedLines + " zero-length linedef(s) removed\n"
		+ removedVertices + " unreferenced vertex/vertices removed\n"
		+ removedSectors + " unreferenced sector(s) removed\n"
		+ mergedVertices + " duplicate vertex/vertices merged\n\n"
		+ "Use Undo if the result is not what you expected.");
}
