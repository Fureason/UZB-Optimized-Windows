/// <reference path="../udbscript.d.ts" />

`#version 5`;

`#name Select control sectors only`;

`#scriptoptions

deselect
{
    description = "Deselect";
    type = 3;
}
`;

const sectors = UDB.Map.getSelectedOrHighlightedSectors();
if (sectors.length === 0)
    UDB.die('No sectors selected');

UDB.Map.clearAllSelected();

sectors.forEach(sector => {
    if (SRB2ControlSector.isFofSector(sector) !== UDB.ScriptOptions.deselect)
        sector.selected = true;
});
