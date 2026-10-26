/// <reference path="../udbscript.d.ts" />

class SRB2VisualMode {
    // Return the Visual Mode selection with a fallback for Classic Mode
    static getSelectionWithFallBack(options) {
        let visualSelection = UDB.Map.srb2_visualSelection;

        if (visualSelection) {
            if (visualSelection.length === 0 && options.highlighted)
                visualSelection = [UDB.Map.srb2_visualHighlightedElement];
        } else {
            const sectors = options.highlighted ? UDB.Map.getSelectedOrHighlightedSectors() : UDB.Map.getSelectedSectors();

            visualSelection = sectors.map(sector => {
                return {
                    type: options.ceiling ? 'ceiling' : 'floor',
                    sector
                };
            });
        }

        return visualSelection;
    };
}
