/// <reference path="../../udbscript.d.ts" />

`#version 5`;

`#name FOF - Clone`;

`#description Clone a FOF into the selected locations`;

const visualSelection = UDB.Map.srb2_visualSelection;
if (!visualSelection)
    UDB.die('You can only use this action in visual mode');
if (visualSelection.length < 2)
    UDB.die('Not enough elements selected. You must select the FOF to be cloned and its target location');

const firstElement = visualSelection[0];
if (!firstElement.type.includes('3dfloor'))
    UDB.die('The first selected element must be the FOF to be cloned.');

const originalFofLine = firstElement.controlLinedef;
const originalFofSector = originalFofLine.front.sector;

const controlSectors = SRB2ControlSector.sortControlSectors(SRB2ControlSector.findUnusedControlSectors());
let controlSectorNum = 0;

UDB.Map.clearAllSelected();

visualSelection.slice(1).forEach((element) => {
    let sector;
    if (element.type === '3dfloor_side' || element.type.startsWith('side'))
        sector = element.sidedef.sector;
    else if (['floor', 'ceiling'].includes(element.type) || element.type.startsWith('3dfloor'))
        sector = element.sector;
    else
        return;

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
