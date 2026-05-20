const maxUploadImageDimension = 1600;
const jpegQuality = 0.78;

document.addEventListener("DOMContentLoaded", () => {
    const form = document.querySelector("[data-verification-form]");
    const fileInput = document.querySelector("[data-upload-input]");
    const dropZone = document.querySelector("[data-drop-zone]");
    const fileMeta = document.querySelector("[data-file-meta]");
    const loading = document.querySelector("[data-loading]");
    const submitButton = document.querySelector("[data-submit-button]");

    if (!form || !fileInput || !dropZone || !fileMeta) {
        return;
    }

    wireSampleButtons(fileInput, fileMeta);

    let optimizationPromise = null;

    fileInput.addEventListener("change", () => {
        optimizationPromise = optimizeSelectedFile(fileInput, fileMeta)
            .finally(() => {
                optimizationPromise = null;
            });
    });

    dropZone.addEventListener("dragover", (event) => {
        event.preventDefault();
        dropZone.classList.add("is-dragging");
    });

    dropZone.addEventListener("dragleave", () => {
        dropZone.classList.remove("is-dragging");
    });

    dropZone.addEventListener("drop", (event) => {
        event.preventDefault();
        dropZone.classList.remove("is-dragging");

        if (!event.dataTransfer?.files?.length) {
            return;
        }

        fileInput.files = event.dataTransfer.files;
        fileInput.dispatchEvent(new Event("change"));
    });

    form.addEventListener("submit", async (event) => {
        if (optimizationPromise) {
            event.preventDefault();
            await optimizationPromise;
            form.requestSubmit();
            return;
        }

        if (loading) {
            loading.hidden = false;
        }

        if (submitButton) {
            submitButton.disabled = true;
            submitButton.textContent = "Verifying...";
        }
    });
});

const sampleLabels = {
    wine: {
        fields: {
            "ExpectedValues.BrandName": "ABC WINERY",
            "ExpectedValues.ClassType": "AMERICAN MERLOT",
            "ExpectedValues.AlcoholContent": "ALC. 15.5% BY VOL.",
            "ExpectedValues.NetContents": "750 ML",
            "ExpectedValues.ProducerBottler": "XYZ VINTNERS",
            "ExpectedValues.CountryOfOrigin": "",
            "ExpectedValues.SulfitesRequirement": "Required",
            "ExpectedValues.RequireCountryOfOrigin": false,
            "ExpectedValues.GovernmentWarning": ""
        },
        text: `Brand: ABC WINERY
Product Name: AMERICAN MERLOT
Type: AMERICAN MERLOT
ABV: ALC. 15.5% BY VOL.
Net Contents: 750 ML
Producer: BOTTLED BY XYZ VINTNERS, CITY, STATE
Government Warning: According to the Surgeon General, women should not drink alcoholic beverages during pregnancy because of the risk of birth defects. Consumption of alcoholic beverages impairs your ability to drive a car or operate machinery, and may cause health problems.
Contains Sulfites`
    },
    spirits: {
        fields: {
            "ExpectedValues.BrandName": "ABC DISTILLERY",
            "ExpectedValues.ClassType": "WHISKY",
            "ExpectedValues.AlcoholContent": "50% ALC/VOL",
            "ExpectedValues.NetContents": "750 ML",
            "ExpectedValues.ProducerBottler": "ABC DISTILLERY",
            "ExpectedValues.CountryOfOrigin": "",
            "ExpectedValues.SulfitesRequirement": "Ignore",
            "ExpectedValues.RequireCountryOfOrigin": false,
            "ExpectedValues.GovernmentWarning": ""
        },
        text: `Brand: ABC DISTILLERY
Product Name: ABC DISTILLERY WHISKY
Type: WHISKY
ABV: 50% ALC/VOL
Net Contents: 750 ML
Producer: DISTILLED AND BOTTLED BY ABC DISTILLERY
Government Warning: According to the Surgeon General, women should not drink alcoholic beverages during pregnancy because of the risk of birth defects. Consumption of alcoholic beverages impairs your ability to drive a car or operate machinery, and may cause health problems.`
    },
    beer: {
        fields: {
            "ExpectedValues.BrandName": "ABC BREWING",
            "ExpectedValues.ClassType": "MALT BEVERAGE",
            "ExpectedValues.AlcoholContent": "5% ALC/VOL",
            "ExpectedValues.NetContents": "12 FL OZ",
            "ExpectedValues.ProducerBottler": "ABC BREWING",
            "ExpectedValues.CountryOfOrigin": "",
            "ExpectedValues.SulfitesRequirement": "Ignore",
            "ExpectedValues.RequireCountryOfOrigin": false,
            "ExpectedValues.GovernmentWarning": ""
        },
        text: `Brand: ABC BREWING
Product Name: ABC LAGER
Type: MALT BEVERAGE
ABV: 5% ALC/VOL
Net Contents: 12 FL OZ
Producer: BREWED AND BOTTLED BY ABC BREWING
Government Warning: According to the Surgeon General, women should not drink alcoholic beverages during pregnancy because of the risk of birth defects. Consumption of alcoholic beverages impairs your ability to drive a car or operate machinery, and may cause health problems.`
    }
};

function wireSampleButtons(fileInput, fileMeta) {
    document.querySelectorAll("[data-sample-label]").forEach((button) => {
        button.addEventListener("click", () => {
            const sample = sampleLabels[button.dataset.sampleLabel];
            if (!sample) {
                return;
            }

            Object.entries(sample.fields).forEach(([name, value]) => {
                const input = document.querySelector(`[name="${name}"]`);
                if (!input) {
                    return;
                }

                if (input.type === "checkbox") {
                    input.checked = Boolean(value);
                } else {
                    input.value = value;
                }
            });

            const ocrText = document.querySelector("[data-ocr-text]");
            if (ocrText) {
                ocrText.value = sample.text;
            }

            fileInput.value = "";
            fileMeta.textContent = "Sample OCR text loaded";
        });
    });
}

async function optimizeSelectedFile(fileInput, fileMeta) {
    const file = fileInput.files?.[0];
    if (!file) {
        fileMeta.textContent = "No file selected";
        return;
    }

    fileMeta.textContent = `${file.name} (${formatBytes(file.size)})`;

    if (!file.type.startsWith("image/")) {
        return;
    }

    try {
        const optimizedFile = await resizeImageFile(file);
        if (!optimizedFile || optimizedFile.size >= file.size) {
            return;
        }

        const dataTransfer = new DataTransfer();
        dataTransfer.items.add(optimizedFile);
        fileInput.files = dataTransfer.files;
        fileMeta.textContent = `${optimizedFile.name} (${formatBytes(optimizedFile.size)}, optimized)`;
    } catch {
        fileMeta.textContent = `${file.name} (${formatBytes(file.size)})`;
    }
}

async function resizeImageFile(file) {
    const image = await loadImage(file);
    const scale = Math.min(
        1,
        maxUploadImageDimension / image.naturalWidth,
        maxUploadImageDimension / image.naturalHeight
    );

    if (scale >= 1 && file.size < 1_500_000) {
        URL.revokeObjectURL(image.src);
        return null;
    }

    const canvas = document.createElement("canvas");
    canvas.width = Math.round(image.naturalWidth * scale);
    canvas.height = Math.round(image.naturalHeight * scale);

    const context = canvas.getContext("2d", { alpha: false });
    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.drawImage(image, 0, 0, canvas.width, canvas.height);
    URL.revokeObjectURL(image.src);

    const blob = await new Promise((resolve) => {
        canvas.toBlob(resolve, "image/jpeg", jpegQuality);
    });

    if (!blob) {
        return null;
    }

    const baseName = file.name.replace(/\.[^.]+$/, "");
    return new File([blob], `${baseName}-optimized.jpg`, {
        type: "image/jpeg",
        lastModified: Date.now()
    });
}

function loadImage(file) {
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = reject;
        image.src = URL.createObjectURL(file);
    });
}

function formatBytes(bytes) {
    if (bytes < 1024) {
        return `${bytes} B`;
    }

    const kilobytes = bytes / 1024;
    if (kilobytes < 1024) {
        return `${kilobytes.toFixed(1)} KB`;
    }

    return `${(kilobytes / 1024).toFixed(1)} MB`;
}
