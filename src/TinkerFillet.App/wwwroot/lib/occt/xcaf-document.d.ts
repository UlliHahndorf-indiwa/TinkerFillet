/**
 * Fluent XCAF document builder for assemblies with colors and names.
 *
 * @example
 * ```ts
 * const doc = XCAFDocument.create(rawKernel);
 * const root = doc.addShape(box, { name: 'housing', color: [0.8, 0.2, 0.1] });
 * doc.addChild(root, gear, {
 *   name: 'gear-1',
 *   location: { tx: 10, ty: 0, tz: 5 },
 *   color: [0.5, 0.5, 0.5],
 * });
 * const step = doc.exportSTEP();
 * const glb = doc.exportGLTF(Module);
 * doc.close();
 * ```
 *
 * Prefer `kernel.createXCAFDocument()` over the static factories. Constructing
 * from a bare raw kernel works, but only a live {@link OcctKernel} registers the
 * module's exception decoder — without one, a failure here reports the
 * undecoded `[object WebAssembly.Exception]` and downgrades to `KERNEL_ERROR`.
 */
import type { ShapeHandle, Color3, LabelTag, LabelInfo, AddShapeOptions, AddChildOptions, GLTFExportOptions } from "./types.js";
/** Raw XCAF methods on the Embind kernel (internal). */
export interface RawXCAFKernel {
    xcafNewDocument(): number;
    xcafClose(docId: number): void;
    xcafAddShape(docId: number, shapeId: number): number;
    xcafAddComponent(docId: number, parentTag: number, shapeId: number, tx: number, ty: number, tz: number, rx: number, ry: number, rz: number): number;
    xcafSetColor(docId: number, tag: number, r: number, g: number, b: number): void;
    xcafSetName(docId: number, tag: number, name: string): void;
    xcafGetLabelInfo(docId: number, tag: number): {
        labelId: number;
        name: string;
        hasColor: boolean;
        r: number;
        g: number;
        b: number;
        isAssembly: boolean;
        isComponent: boolean;
        shapeId: number;
    };
    xcafGetChildLabels(docId: number, parentTag: number): {
        size(): number;
        get(i: number): number;
        delete(): void;
    };
    xcafGetRootLabels(docId: number): {
        size(): number;
        get(i: number): number;
        delete(): void;
    };
    xcafExportSTEP(docId: number): string;
    xcafImportSTEP(stepData: string): number;
    xcafExportGLTF(docId: number, linDefl: number, angDefl: number): string;
}
/** Emscripten FS interface needed for binary glTF export. */
export interface EmscriptenFS {
    readFile(path: string): Uint8Array;
    writeFile(path: string, data: Uint8Array): void;
    unlink(path: string): void;
}
export declare class XCAFDocument {
    #private;
    private constructor();
    /** Create a new empty XCAF document. */
    static create(raw: RawXCAFKernel, fs?: EmscriptenFS): XCAFDocument;
    /** Import a STEP file into a new XCAF document (preserves colors/names/assemblies). */
    static fromSTEP(raw: RawXCAFKernel, stepData: string, fs?: EmscriptenFS): XCAFDocument;
    /** Add a shape as a root label. */
    addShape(shape: ShapeHandle, options?: AddShapeOptions): LabelTag;
    /** Add a shape as a child component of a parent label. */
    addChild(parent: LabelTag, shape: ShapeHandle, options?: AddChildOptions): LabelTag;
    /** Set color on an existing label. */
    setColor(label: LabelTag, color: Color3): void;
    /** Set name on an existing label. */
    setName(label: LabelTag, name: string): void;
    /**
     * Get info about a label.
     * If `shapeHandle` is non-null, the caller owns it and must release it.
     */
    getLabelInfo(label: LabelTag): LabelInfo;
    /** Get child label tags of a parent. */
    getChildren(parent: LabelTag): LabelTag[];
    /** Get root (free) shape label tags. */
    getRoots(): LabelTag[];
    /** Export as STEP with colors and names preserved. */
    exportSTEP(): string;
    /**
     * Export as glTF binary (.glb). Returns raw bytes as Uint8Array.
     *
     * When the document was created via `OcctKernel.createXCAFDocument()`,
     * the Emscripten FS is injected automatically. Otherwise, pass it
     * explicitly or via the options object.
     */
    exportGLTF(options?: GLTFExportOptions & {
        fs?: EmscriptenFS;
    }): Uint8Array;
    /** @deprecated Pass `fs` via options instead: `exportGLTF({ fs })` */
    exportGLTF(fs: EmscriptenFS, options?: GLTFExportOptions): Uint8Array;
    /** Close the document and free OCCT resources. */
    close(): void;
    [Symbol.dispose](): void;
}
//# sourceMappingURL=xcaf-document.d.ts.map