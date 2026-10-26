/// <reference path="../../udbscript.d.ts" />

`#version 5`;

`#name Copy plane from opposite surface`;

`#scriptoptions

surfaceType
{
	description = "Affected surface type in 2D view";
	default = 0;
	type = 11; // Enum
	enumvalues {
		0 = "Floor";
		1 = "Ceiling";
	}
}
`;

const visualSelection = SRB2VisualMode.getSelectionWithFallBack({
	highlighted: true,
	ceiling: (UDB.ScriptOptions.surfaceType === 1)
});

if (visualSelection.length === 0)
    UDB.die('No surface selected');

visualSelection.forEach(element => {
	if (['floor', '3dfloor_bottom'].includes(element.type)) {
		const sector = (element.type === '3dfloor_bottom') ? element.controlLinedef.front.sector : element.sector;
		sector.floorHeight = sector.ceilingHeight;
		Misc.setSectorFloorPlane(sector, Misc.getReversedPlane(sector.getCeilingPlane()));
	} else if (['ceiling', '3dfloor_top'].includes(element.type)) {
		const sector = (element.type === '3dfloor_top') ? element.controlLinedef.front.sector : element.sector;
		sector.ceilingHeight = sector.floorHeight;
		Misc.setSectorCeilingPlane(sector, Misc.getReversedPlane(sector.getFloorPlane()));
	} else {
        UDB.die('One of the selected elements is not a sector surface.');
	}
});
