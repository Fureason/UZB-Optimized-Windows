/// <reference path="../../udbscript.d.ts" />

`#version 5`;

`#name FOF - Extrude`;

`#description Extend FOFs on selected sides`;

if (!UDB.Map.srb2_visualSelection)
    UDB.die('You can only use this action in visual mode');

const visualSelection = UDB.Map.srb2_visualSelection.length > 0 ? UDB.Map.srb2_visualSelection : UDB.Map.srb2_visualHighlightedElement ? [UDB.Map.srb2_visualHighlightedElement] : [];

if (visualSelection.length === 0)
    UDB.die('No FOF sides selected');

visualSelection.forEach(element => {
    if (element.type !== '3dfloor_side')
        UDB.die('One of the selected elements is not a FOF side');

    element.sidedef.sector.addTag(element.controlLinedef.args[0]);
});
