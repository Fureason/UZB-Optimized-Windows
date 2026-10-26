/// <reference path="../../udbscript.d.ts" />

`#version 5`;

`#name FOF - Select control linedefs`;

`#description Select the control linedefs of the selected sectors or FOFs`;

const selectLine = (line) => {
    if (SRB2ControlSector.isFofLine(line) && line.front) {
        // In Visual Mode select the actual control wall surface. Selecting the
        // map linedef itself does not populate the Visual Mode selection and
        // makes commands that operate on selected surfaces appear to do nothing.
        line.front.middleSelected = true;
    }
};

const elements = UDB.Map.srb2_visualSelection;
if (!elements)
    UDB.die('You can only use this action in visual mode.');

UDB.Map.clearAllSelected();

// Gather all selected sector tags first, then scan the map's linedefs once.
// The old per-tag filter rescanned every linedef for every selected sector and
// became noticeably slow on large SRB2 maps.
const selectedSectorTags = {};
elements.forEach(element => {
    if (['floor', 'ceiling'].includes(element.type)) {
        element.sector.getTags().forEach(tag => {
            selectedSectorTags[tag] = true;
        });
    } else if (element.type.startsWith('3dfloor')) {
        selectLine(element.controlLinedef);
    }
});

UDB.Map.getLinedefs().forEach(line => {
    if (selectedSectorTags[line.args[0]] === true) selectLine(line);
});
