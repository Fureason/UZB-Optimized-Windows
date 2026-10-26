/// <reference path="../../udbscript.d.ts" />

`#version 5`;

`#name FOF - Clone in-place`;

`#description Clone a FOF at its current location`;

const visualSelection = UDB.Map.srb2_visualSelection;
if (!visualSelection)
    UDB.die('You can only use this action in visual mode');
if (visualSelection.length < 1)
    UDB.die('No FOFs selected');

const controlSectors = SRB2ControlSector.sortControlSectors(SRB2ControlSector.findUnusedControlSectors());
let controlSectorNum = 0;

UDB.Map.clearAllSelected();

visualSelection.forEach((element) => {
    let sector;
    if (element.type === '3dfloor_side')
        sector = element.sidedef.other.sector;
    else if (element.type.startsWith('3dfloor'))
        sector = element.sector;
    else
        UDB.die('One of the selected elements is not a FOF');

    const originalFofLine = element.controlLinedef;
    const originalFofSector = originalFofLine.front.sector;

    const fofSector = controlSectors[controlSectorNum];
    if (!fofSector)
        UDB.die('Not enough control sectors available');
    controlSectorNum++;

    const fofLines = fofSector.getSidedefs().map(s => s.line);
    const fofLine = SRB2ControlSector.findBestControlLine(fofLines);

    const tag = UDB.Map.getNewTag();

    sector.addTag(tag);

    originalFofLine.copyPropertiesTo(fofLine);
    originalFofSector.copyPropertiesTo(fofSector);
    fofLine.args[0] = tag;

    fofLine.front.middleTexture = originalFofLine.front.middleTexture;

    fofSector.floorSelected = true;
    fofSector.ceilingSelected = true;
});
