/// <reference path="../../udbscript.d.ts" />

`#version 5`;

`#name FOF - Delete`;

`#description Delete the selected FOFs`;

const clearAction = (line) => {
    line.action = 0;

    for (let i = 0; i < line.args.length; i++)
        line.args[i] = 0;

    line.fields.stringarg0 = null;
    line.fields.stringarg1 = null;
};

if (!UDB.Map.srb2_visualSelection)
    UDB.die('You can only use this action in visual mode.');

const visualSelection = UDB.Map.srb2_visualSelection.length > 0 ? UDB.Map.srb2_visualSelection : UDB.Map.srb2_visualHighlightedElement ? [UDB.Map.srb2_visualHighlightedElement] : [];

const tagToClearableLines = new Map();

visualSelection.forEach((element) => {
    if (!element.type.startsWith('3dfloor'))
        return;

    const fofLine = element.controlLinedef;
    const tag = fofLine.args[0];

    element.sector.removeTag(tag);

    if (tagToClearableLines.has(tag))
        tagToClearableLines.get(tag).push(fofLine);
    else
        tagToClearableLines.set(tag, [fofLine]);
});

if (tagToClearableLines.size === 0)
    UDB.die('No FOFs selected.');

UDB.Map.getSectors().forEach(sector => {
    sector.getTags().forEach(t => tagToClearableLines.delete(t));
});

tagToClearableLines.forEach(fofLines => fofLines.forEach(fofLine => {
    clearAction(fofLine);
    fofLine.getTags().forEach(t => fofLine.removeTag(t));
    fofLine.clearFlags();

    if (false) {
        const fofSector = fofLine.front.sector;

        fofSector.getTags().forEach(t => fofSector.removeTag(t));
        fofSector.clearFlags();
    }
}));
