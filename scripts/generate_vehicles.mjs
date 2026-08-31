#!/usr/bin/env node

import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

function mulberry32(seed) {
  return function next() {
    let t = (seed += 0x6d2b79f5);
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const rng = mulberry32(42);
const CURRENT_YEAR = new Date().getFullYear();
const AUCTION_HOURS = [9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20];
const PROVINCE_WEIGHTS = {
  Ontario: 40,
  Quebec: 23,
  "British Columbia": 14,
  Alberta: 12,
  Manitoba: 4,
  Saskatchewan: 3,
  "Nova Scotia": 2,
  "New Brunswick": 2,
};

const MAKES_MODELS = {
  Toyota: {
    Camry: { body: "sedan", engines: ["2.5L I4", "2.5L Hybrid I4", "3.5L V6"], trims: ["LE", "SE", "XLE", "XSE", "TRD"] },
    RAV4: { body: "SUV", engines: ["2.5L I4", "2.5L Hybrid I4"], trims: ["LE", "XLE", "XLE Premium", "Adventure", "TRD Off-Road"] },
    Corolla: { body: "sedan", engines: ["1.8L I4", "2.0L I4", "1.8L Hybrid I4"], trims: ["L", "LE", "SE", "XLE", "XSE"] },
    Highlander: { body: "SUV", engines: ["2.5L Hybrid I4", "3.5L V6"], trims: ["L", "LE", "XLE", "Limited", "Platinum"] },
    Tacoma: { body: "truck", engines: ["2.7L I4", "3.5L V6"], trims: ["SR", "SR5", "TRD Sport", "TRD Off-Road", "Limited"] },
    Tundra: { body: "truck", engines: ["3.5L Twin-Turbo V6", "3.5L Hybrid Twin-Turbo V6"], trims: ["SR", "SR5", "Limited", "Platinum", "1794 Edition"] },
  },
  Honda: {
    Civic: { body: "sedan", engines: ["2.0L I4", "1.5L Turbo I4"], trims: ["LX", "Sport", "EX", "Touring"] },
    "CR-V": { body: "SUV", engines: ["1.5L Turbo I4", "2.0L Hybrid I4"], trims: ["LX", "EX", "EX-L", "Touring"] },
    Accord: { body: "sedan", engines: ["1.5L Turbo I4", "2.0L Turbo I4", "2.0L Hybrid I4"], trims: ["LX", "Sport", "EX-L", "Touring"] },
    Pilot: { body: "SUV", engines: ["3.5L V6"], trims: ["LX", "Sport", "EX-L", "Touring", "Elite"] },
  },
  Ford: {
    "F-150": { body: "truck", engines: ["2.7L EcoBoost V6", "3.5L EcoBoost V6", "5.0L V8", "3.5L PowerBoost Hybrid V6"], trims: ["XL", "XLT", "Lariat", "King Ranch", "Platinum"] },
    Escape: { body: "SUV", engines: ["1.5L EcoBoost I3", "2.0L EcoBoost I4", "2.5L Hybrid I4"], trims: ["S", "SE", "SEL", "Titanium"] },
    Explorer: { body: "SUV", engines: ["2.3L EcoBoost I4", "3.0L EcoBoost V6", "3.3L Hybrid V6"], trims: ["Base", "XLT", "Limited", "ST", "Platinum"] },
    Mustang: { body: "coupe", engines: ["2.3L EcoBoost I4", "5.0L V8"], trims: ["EcoBoost", "EcoBoost Premium", "GT", "GT Premium", "Mach 1"] },
    Bronco: { body: "SUV", engines: ["2.3L EcoBoost I4", "2.7L EcoBoost V6"], trims: ["Base", "Big Bend", "Black Diamond", "Outer Banks", "Wildtrak", "Badlands"] },
  },
  Chevrolet: {
    "Silverado 1500": { body: "truck", engines: ["2.7L Turbo I4", "5.3L V8", "6.2L V8", "3.0L Duramax Diesel I6"], trims: ["WT", "Custom", "LT", "RST", "LT Trail Boss", "LTZ", "High Country"] },
    Equinox: { body: "SUV", engines: ["1.5L Turbo I4"], trims: ["LS", "LT", "RS", "Premier"] },
    Malibu: { body: "sedan", engines: ["1.5L Turbo I4"], trims: ["LS", "RS", "LT", "Premier"] },
    Traverse: { body: "SUV", engines: ["3.6L V6"], trims: ["LS", "LT", "RS", "Premier", "High Country"] },
  },
  BMW: {
    "3 Series": { body: "sedan", engines: ["2.0L Turbo I4", "3.0L Turbo I6"], trims: ["330i", "330i xDrive", "M340i", "M340i xDrive"] },
    X3: { body: "SUV", engines: ["2.0L Turbo I4", "3.0L Turbo I6"], trims: ["sDrive30i", "xDrive30i", "M40i"] },
    X5: { body: "SUV", engines: ["3.0L Turbo I6", "4.4L Twin-Turbo V8"], trims: ["xDrive40i", "xDrive45e", "M50i"] },
    "5 Series": { body: "sedan", engines: ["2.0L Turbo I4", "3.0L Turbo I6", "4.4L Twin-Turbo V8"], trims: ["530i", "530i xDrive", "540i", "540i xDrive", "M550i"] },
  },
  Tesla: {
    "Model 3": { body: "sedan", engines: ["Electric Single Motor", "Electric Dual Motor"], trims: ["Standard Range Plus", "Long Range", "Performance"] },
    "Model Y": { body: "SUV", engines: ["Electric Dual Motor"], trims: ["Long Range", "Performance"] },
    "Model S": { body: "sedan", engines: ["Electric Dual Motor", "Electric Tri Motor"], trims: ["Long Range", "Plaid"] },
  },
  Ram: {
    "1500": { body: "truck", engines: ["3.6L V6", "5.7L HEMI V8", "3.0L EcoDiesel V6"], trims: ["Tradesman", "Big Horn", "Laramie", "Rebel", "Limited", "TRX"] },
  },
  Hyundai: {
    Tucson: { body: "SUV", engines: ["2.5L I4", "1.6L Turbo Hybrid I4"], trims: ["SE", "SEL", "N Line", "Limited"] },
    Elantra: { body: "sedan", engines: ["2.0L I4", "1.6L Turbo I4", "1.6L Hybrid I4"], trims: ["SE", "SEL", "N Line", "Limited", "N"] },
    "Santa Fe": { body: "SUV", engines: ["2.5L I4", "2.5L Turbo I4", "1.6L Turbo Hybrid I4"], trims: ["SE", "SEL", "XRT", "Limited", "Calligraphy"] },
    "Ioniq 5": { body: "SUV", engines: ["Electric Single Motor", "Electric Dual Motor"], trims: ["SE Standard Range", "SE Long Range", "SEL", "Limited"] },
  },
  Kia: {
    Forte: { body: "sedan", engines: ["2.0L I4", "1.6L Turbo I4"], trims: ["FE", "LXS", "GT-Line", "GT"] },
    Sportage: { body: "SUV", engines: ["2.5L I4", "1.6L Turbo Hybrid I4"], trims: ["LX", "EX", "SX", "SX Prestige", "X-Pro"] },
    Telluride: { body: "SUV", engines: ["3.8L V6"], trims: ["LX", "S", "EX", "SX", "SX Prestige", "X-Pro"] },
  },
  Mazda: {
    "CX-5": { body: "SUV", engines: ["2.5L I4", "2.5L Turbo I4"], trims: ["Sport", "Select", "Preferred", "Carbon Edition", "Turbo", "Signature"] },
    Mazda3: { body: "sedan", engines: ["2.0L I4", "2.5L I4", "2.5L Turbo I4"], trims: ["GX", "GS", "GT", "Turbo"] },
  },
  Subaru: {
    Outback: { body: "SUV", engines: ["2.5L I4", "2.4L Turbo I4"], trims: ["Base", "Premium", "Limited", "Touring", "Wilderness", "Onyx Edition XT"] },
    Crosstrek: { body: "SUV", engines: ["2.0L I4", "2.5L I4"], trims: ["Base", "Premium", "Sport", "Limited"] },
    WRX: { body: "sedan", engines: ["2.4L Turbo I4"], trims: ["Base", "Premium", "Limited", "GT"] },
  },
  Volkswagen: {
    Jetta: { body: "sedan", engines: ["1.5L Turbo I4"], trims: ["S", "Sport", "SE", "SEL"] },
    Tiguan: { body: "SUV", engines: ["2.0L Turbo I4"], trims: ["S", "SE", "SE R-Line", "SEL", "SEL R-Line"] },
    "Golf GTI": { body: "hatchback", engines: ["2.0L Turbo I4"], trims: ["S", "SE", "Autobahn"] },
  },
  GMC: {
    "Sierra 1500": { body: "truck", engines: ["2.7L Turbo I4", "5.3L V8", "6.2L V8", "3.0L Duramax Diesel I6"], trims: ["Pro", "SLE", "Elevation", "SLT", "AT4", "Denali"] },
    Terrain: { body: "SUV", engines: ["1.5L Turbo I4"], trims: ["SLE", "SLT", "AT4", "Denali"] },
  },
  Jeep: {
    Wrangler: { body: "SUV", engines: ["2.0L Turbo I4", "3.6L V6", "3.0L EcoDiesel V6"], trims: ["Sport", "Sport S", "Sahara", "Rubicon"] },
    "Grand Cherokee": { body: "SUV", engines: ["2.0L Turbo I4", "3.6L V6", "5.7L HEMI V8"], trims: ["Laredo", "Limited", "Overland", "Summit", "Trailhawk"] },
  },
  Nissan: {
    Rogue: { body: "SUV", engines: ["1.5L Turbo I3"], trims: ["S", "SV", "SL", "Platinum"] },
    Altima: { body: "sedan", engines: ["2.5L I4", "2.0L Turbo I4"], trims: ["S", "SV", "SR", "SL", "Platinum"] },
    Pathfinder: { body: "SUV", engines: ["3.5L V6"], trims: ["S", "SV", "SL", "Platinum", "Rock Creek"] },
  },
};

const EXTERIOR_COLORS = [
  "White", "Black", "Silver", "Grey", "Red", "Blue", "Dark Blue", "Midnight Blue", "Pearl White",
  "Glacier White", "Magnetic Grey", "Oxford White", "Shadow Black", "Rapid Red", "Iconic Silver",
  "Celestial Silver", "Lunar Silver", "Crystal Black", "Platinum White", "Burgundy", "Green",
  "Dark Green", "Bronze", "Champagne", "Orange",
];

const INTERIOR_COLORS = [
  "Black", "Grey", "Dark Grey", "Light Grey", "Tan", "Beige", "Brown", "Saddle Brown", "Cognac",
  "Red", "White", "Cream",
];

const PROVINCES_CITIES = {
  Ontario: ["Toronto", "Ottawa", "Mississauga", "Brampton", "Hamilton", "London", "Markham", "Vaughan", "Kitchener", "Windsor", "Barrie"],
  "British Columbia": ["Vancouver", "Surrey", "Burnaby", "Richmond", "Kelowna", "Victoria", "Abbotsford", "Langley"],
  Alberta: ["Calgary", "Edmonton", "Red Deer", "Lethbridge", "Airdrie"],
  Quebec: ["Montreal", "Quebec City", "Laval", "Gatineau", "Longueuil", "Sherbrooke"],
  Manitoba: ["Winnipeg", "Brandon"],
  Saskatchewan: ["Saskatoon", "Regina"],
  "Nova Scotia": ["Halifax", "Dartmouth"],
  "New Brunswick": ["Moncton", "Saint John", "Fredericton"],
};

const DEALERSHIPS_BY_PROVINCE = {
  Ontario: [
    "AutoPark Toronto", "Capital City Auto", "Durham Auto Exchange", "Golden Horseshoe Motors",
    "Grand Touring Motors", "Highway 7 Auto Sales", "King City Auto", "Lakeshore Auto Group",
    "Maple Motors", "Northland Chrysler", "Parkway Ford",
  ],
  Quebec: [
    "Champlain Motors", "Laurentian Auto Group", "Metropolitain Auto", "Rive-Sud Motors",
    "St-Laurent Auto Exchange",
  ],
  "British Columbia": [
    "Fraser Valley Auto Group", "Harbour City Auto", "Island Auto Exchange", "Mountain View Motors",
    "Pacific Gateway Motors", "Westcoast Auto Sales",
  ],
  Alberta: [
    "Chinook Auto Group", "Foothills Auto Sales", "Northern Ridge Motors", "Prairie Motors",
    "Stampede Auto Exchange",
  ],
  Manitoba: ["Keystone Auto Group", "Prairie Sun Motors"],
  Saskatchewan: ["Grain Belt Auto", "Horizon Motors"],
  "Nova Scotia": ["Atlantic Auto Exchange", "Harbourfront Motors"],
  "New Brunswick": ["Acadian Auto Exchange", "Fundy Motors"],
};

const DAMAGE_ITEMS = [
  "Minor scratches on front bumper",
  "Small dent on driver-side door",
  "Curb rash on front-right wheel",
  "Chip in windshield (passenger side)",
  "Scratch on rear bumper from parking",
  "Paint touch-up on hood (rock chips)",
  "Minor scrape on passenger-side mirror",
  "Light hail damage on roof",
  "Dent on rear quarter panel",
  "Scratch along driver-side fender",
  "Worn leather on driver seat bolster",
  "Crack in rear taillight housing",
  "Small tear in rear seat upholstery",
  "Scuff marks on door sill plates",
  "Faded headlight lenses",
  "Minor rust on wheel wells",
  "Dent on tailgate",
  "Scratch on liftgate",
  "Paint peeling on roof rack",
  "Cracked front grille",
  "Missing wheel center cap",
  "Damaged mud flap (rear left)",
  "Worn brake rotors - due for replacement",
  "AC compressor intermittent - needs diagnosis",
  "Check engine light - catalytic converter code",
  "Transmission slips between 2nd and 3rd gear",
  "Water damage to rear cargo area carpet",
  "Frame damage - right front (repaired)",
  "Airbag deployed - replaced with OEM parts",
  "Flood damage - electrical issues present",
];

const CONDITION_REPORTS = [
  {
    min: 4.0,
    max: 5.0,
    templates: [
      "Vehicle is in excellent condition overall. Interior is clean with minimal wear. Exterior shows only minor imperfections consistent with age.",
      "Well-maintained vehicle with full service history available. Paint is in very good condition. Interior shows light use consistent with mileage.",
      "Above-average condition for year and mileage. All mechanical systems functioning properly. Recently serviced with new tires.",
      "Exceptional condition. Single previous owner. Garage kept. No mechanical issues reported.",
      "Very clean vehicle inside and out. Minor cosmetic wear only. All electronics and features fully functional.",
    ],
  },
  {
    min: 3.0,
    max: 3.9,
    templates: [
      "Vehicle shows average wear for its age and mileage. Some cosmetic imperfections noted. Mechanically sound.",
      "Fair condition overall. Interior has some wear but no major damage. A few exterior blemishes. Drives well.",
      "Average condition. Has some visible wear on high-touch surfaces. Engine and transmission perform within normal parameters.",
      "Reasonable condition with expected wear. Would benefit from detailing. No major mechanical concerns identified.",
      "Moderate wear throughout. Previous daily driver. Runs and drives without issue. Cosmetic touch-ups recommended.",
    ],
  },
  {
    min: 2.0,
    max: 2.9,
    templates: [
      "Vehicle shows significant wear. Multiple cosmetic issues noted. Mechanical inspection recommended before purchase.",
      "Below-average condition. Visible damage present. May require immediate maintenance to address noted issues.",
      "Condition reflects heavy use. Several repairs recommended. Priced accordingly to account for needed work.",
      "Rough condition with notable damage. Suitable for buyer comfortable with repairs. Core mechanical systems operational.",
    ],
  },
  {
    min: 1.0,
    max: 1.9,
    templates: [
      "Vehicle has substantial damage. Sold as-is. Recommended for parts or significant rebuild only.",
      "Major mechanical and cosmetic issues present. Not roadworthy in current state. For experienced rebuilders.",
      "Extensive damage from incident. Frame integrity should be professionally assessed. Salvage title.",
    ],
  },
];

function rand() {
  return rng();
}

function randomBetween(min, max) {
  return min + rand() * (max - min);
}

function randInt(min, max) {
  return Math.floor(randomBetween(min, max + 1));
}

function choice(values) {
  return values[randInt(0, values.length - 1)];
}

function weightedChoice(weightMap) {
  const entries = Object.entries(weightMap);
  const total = entries.reduce((sum, [, weight]) => sum + weight, 0);
  let target = randomBetween(0, total);

  for (const [value, weight] of entries) {
    target -= weight;
    if (target <= 0) {
      return value;
    }
  }

  return entries[entries.length - 1][0];
}

function roundToNearest500(value) {
  return Math.round(value / 500) * 500;
}

function generateVin() {
  const chars = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";
  let vin = "";

  for (let index = 0; index < 17; index += 1) {
    vin += index === 8 ? String(randInt(0, 9)) : choice(chars.split(""));
  }

  return vin;
}

function getFuelType(engine) {
  if (engine.includes("Electric")) {
    return "electric";
  }

  if (engine.includes("Hybrid")) {
    return "hybrid";
  }

  if (engine.includes("Diesel") || engine.includes("EcoDiesel") || engine.includes("Duramax")) {
    return "diesel";
  }

  return "gasoline";
}

function getTransmission(engine, model, bodyStyle) {
  if (engine.includes("Electric")) {
    return "single-speed";
  }

  const manualEligible = bodyStyle === "coupe" || ["WRX", "Golf GTI", "Mustang"].includes(model);
  if (manualEligible) {
    return choice(["manual", "automatic", "automatic", "automatic"]);
  }

  return choice(["automatic", "automatic", "automatic", "CVT"]);
}

function getDrivetrain(make, model, bodyStyle) {
  if (make === "Tesla") {
    return choice(["AWD", "RWD"]);
  }

  if (make === "Subaru") {
    return "AWD";
  }

  if (bodyStyle === "truck") {
    return choice(["4WD", "4WD", "RWD", "AWD"]);
  }

  if (["Wrangler", "Bronco"].includes(model)) {
    return "4WD";
  }

  if (make === "BMW") {
    return choice(["AWD", "RWD"]);
  }

  if (bodyStyle === "SUV") {
    return choice(["AWD", "FWD", "AWD", "4WD"]);
  }

  return choice(["FWD", "FWD", "AWD"]);
}

function generateCondition(year, odometerKm, titleStatus) {
  const age = CURRENT_YEAR - year;
  let base = 4.5 - age * 0.15 - odometerKm / 200000;

  if (titleStatus === "salvage") {
    base = randomBetween(1.0, 2.5);
  } else if (titleStatus === "rebuilt") {
    base = randomBetween(2.0, 3.5);
  }

  const grade = Math.round(Math.max(1.0, Math.min(5.0, base + randomBetween(-0.5, 0.5))) * 10) / 10;
  const reportGroup = CONDITION_REPORTS.find((group) => grade >= group.min && grade <= group.max) ?? CONDITION_REPORTS[1];
  const report = choice(reportGroup.templates);

  let damageNotes = [];
  if (grade < 2.0) {
    damageNotes = [
      ...sampleWithoutReplacement(DAMAGE_ITEMS.slice(22), randInt(1, 3)),
      ...sampleWithoutReplacement(DAMAGE_ITEMS.slice(0, 22), randInt(1, 3)),
    ];
  } else if (grade < 3.0) {
    damageNotes = sampleWithoutReplacement(DAMAGE_ITEMS.slice(0, 25), randInt(2, 5));
  } else if (grade < 4.0 && rand() < 0.6) {
    damageNotes = sampleWithoutReplacement(DAMAGE_ITEMS.slice(0, 22), randInt(1, 3));
  } else if (grade >= 4.0 && rand() < 0.2) {
    damageNotes = sampleWithoutReplacement(DAMAGE_ITEMS.slice(0, 15), 1);
  }

  return {
    conditionGrade: grade,
    conditionReport: report,
    damageNotes,
  };
}

function estimatePrice(year, make, model, odometerKm, conditionGrade, titleStatus) {
  const premiumTrucks = ["F-150", "Silverado 1500", "Sierra 1500", "1500", "Tundra"];
  const midTrucks = ["Tacoma"];
  const premiumSuvs = ["X5", "Grand Cherokee", "Telluride", "Highlander", "Pilot", "Explorer"];
  const compactSuvs = ["CR-V", "RAV4", "Tucson", "Sportage", "Equinox", "Escape", "CX-5", "Crosstrek", "Rogue", "Tiguan", "Terrain", "Bronco"];
  let base = randInt(25000, 40000);

  if (make === "Tesla" && model === "Model S") {
    base = randInt(85000, 120000);
  } else if (make === "Tesla") {
    base = randInt(50000, 75000);
  } else if (make === "BMW" && ["X5", "5 Series"].includes(model)) {
    base = randInt(65000, 95000);
  } else if (make === "BMW") {
    base = randInt(48000, 70000);
  } else if (premiumTrucks.includes(model)) {
    base = randInt(50000, 80000);
  } else if (midTrucks.includes(model)) {
    base = randInt(38000, 55000);
  } else if (premiumSuvs.includes(model)) {
    base = randInt(45000, 70000);
  } else if (compactSuvs.includes(model)) {
    base = randInt(32000, 48000);
  } else if (["Mustang", "WRX", "Golf GTI"].includes(model)) {
    base = randInt(35000, 55000);
  } else if (model === "Wrangler") {
    base = randInt(42000, 60000);
  }

  const age = CURRENT_YEAR - year;
  let depreciation = 0.92;
  if (age === 1) {
    depreciation = 0.82;
  } else if (age === 2) {
    depreciation = 0.73;
  } else if (age > 2) {
    depreciation = 0.73 - (age - 2) * 0.055;
  }
  depreciation = Math.max(0.3, depreciation);

  const expectedKm = age > 0 ? age * 18000 : 5000;
  const mileageRatio = odometerKm / Math.max(expectedKm, 5000);
  let mileageFactor = 1.0;
  if (mileageRatio < 0.8) {
    mileageFactor = 1.05;
  } else if (mileageRatio >= 1.2 && mileageRatio < 1.5) {
    mileageFactor = 0.92;
  } else if (mileageRatio >= 1.5) {
    mileageFactor = 0.8;
  }

  const conditionFactor = 0.7 + (conditionGrade / 5) * 0.35;
  const titleFactor = titleStatus === "salvage" ? 0.45 : titleStatus === "rebuilt" ? 0.68 : 1;
  const wholesaleValue = Math.max(2500, Math.floor(base * depreciation * mileageFactor * conditionFactor * titleFactor));

  const startingBid = Math.max(1000, roundToNearest500(wholesaleValue * randomBetween(0.5, 0.75)));
  const reservePrice = rand() < 0.7 ? roundToNearest500(wholesaleValue * randomBetween(0.85, 0.98)) : null;
  const buyNowPrice = rand() < 0.2 ? roundToNearest500(wholesaleValue * randomBetween(1.02, 1.18)) : null;

  return { startingBid, reservePrice, buyNowPrice };
}

function generateBidState(startingBid, reservePrice, buyNowPrice) {
  if (rand() >= 0.38) {
    return { currentBid: null, bidCount: 0 };
  }

  const bidCount = randInt(1, 18);
  const softCeiling = reservePrice ?? buyNowPrice ?? roundToNearest500(startingBid * randomBetween(1.3, 2.4));
  let currentBid = roundToNearest500(Math.max(startingBid + 500, softCeiling * randomBetween(0.72, 1.02)));

  if (buyNowPrice) {
    currentBid = Math.min(currentBid, buyNowPrice - 500);
  }

  currentBid = Math.max(startingBid + 500, currentBid);

  return {
    currentBid,
    bidCount,
  };
}

function sampleWithoutReplacement(values, count) {
  const pool = [...values];
  const result = [];

  while (pool.length > 0 && result.length < count) {
    result.push(pool.splice(randInt(0, pool.length - 1), 1)[0]);
  }

  return result;
}

function pickProvinceAndCity() {
  const province = weightedChoice(PROVINCE_WEIGHTS);
  return {
    province,
    city: choice(PROVINCES_CITIES[province]),
  };
}

function formatLocalDateTime(date) {
  const year = String(date.getFullYear());
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  const hours = String(date.getHours()).padStart(2, "0");
  return `${year}-${month}-${day}T${hours}:00:00`;
}

function generateAuctionStart() {
  const base = new Date();
  base.setDate(base.getDate() + 1 + randInt(0, 6));
  base.setHours(choice(AUCTION_HOURS), 0, 0, 0);
  return formatLocalDateTime(base);
}

function lotNumberForIndex(index) {
  const lane = String.fromCharCode(65 + Math.floor(index / 50));
  return `${lane}-${String((index % 50) + 1).padStart(4, "0")}`;
}

function generateVehicle(index) {
  const make = choice(Object.keys(MAKES_MODELS));
  const model = choice(Object.keys(MAKES_MODELS[make]));
  const info = MAKES_MODELS[make][model];
  const year = randInt(CURRENT_YEAR - 10, CURRENT_YEAR);
  const trim = choice(info.trims);
  const engine = choice(info.engines);
  const bodyStyle = info.body;
  const fuelType = getFuelType(engine);
  const transmission = getTransmission(engine, model, bodyStyle);
  const drivetrain = getDrivetrain(make, model, bodyStyle);

  let odometerKm = Math.max(
    500,
    Math.floor((CURRENT_YEAR - year) * randInt(12000, 25000) * randomBetween(0.7, 1.3)),
  );
  if (fuelType === "electric") {
    odometerKm = Math.floor(odometerKm * 0.7);
  }

  const titleRoll = rand();
  const titleStatus = titleRoll < 0.08 ? "salvage" : titleRoll < 0.15 ? "rebuilt" : "clean";
  const { conditionGrade, conditionReport, damageNotes } = generateCondition(year, odometerKm, titleStatus);
  const { province, city } = pickProvinceAndCity();
  const { startingBid, reservePrice, buyNowPrice } = estimatePrice(year, make, model, odometerKm, conditionGrade, titleStatus);
  const { currentBid, bidCount } = generateBidState(startingBid, reservePrice, buyNowPrice);
  const imageCount = randInt(3, 6);

  return {
    id: crypto.randomUUID(),
    vin: generateVin(),
    year,
    make,
    model,
    trim,
    body_style: bodyStyle,
    exterior_color: choice(EXTERIOR_COLORS),
    interior_color: choice(INTERIOR_COLORS),
    engine,
    transmission,
    drivetrain,
    odometer_km: odometerKm,
    fuel_type: fuelType,
    condition_grade: conditionGrade,
    condition_report: conditionReport,
    damage_notes: damageNotes,
    title_status: titleStatus,
    province,
    city,
    auction_start: generateAuctionStart(),
    starting_bid: startingBid,
    reserve_price: reservePrice,
    buy_now_price: buyNowPrice,
    images: Array.from({ length: imageCount }, (_, imageIndex) => (
      `https://placehold.co/800x600/1a1a2e/eaeaea?text=${year}+${make.replaceAll(" ", "+")}+${model.replaceAll(" ", "+")}+Photo+${imageIndex + 1}`
    )),
    selling_dealership: choice(DEALERSHIPS_BY_PROVINCE[province]),
    lot: lotNumberForIndex(index),
    current_bid: currentBid,
    bid_count: bidCount,
  };
}

function summarize(vehicles) {
  return {
    count: vehicles.length,
    withCurrentBid: vehicles.filter((vehicle) => vehicle.current_bid !== null).length,
    withBuyNow: vehicles.filter((vehicle) => vehicle.buy_now_price !== null).length,
    titleStatus: vehicles.reduce((accumulator, vehicle) => {
      accumulator[vehicle.title_status] = (accumulator[vehicle.title_status] ?? 0) + 1;
      return accumulator;
    }, {}),
  };
}

const vehicles = Array.from({ length: 200 }, (_, index) => generateVehicle(index));
const scriptDir = dirname(fileURLToPath(import.meta.url));
const outputPath = resolve(scriptDir, "../data/vehicles.json");

mkdirSync(dirname(outputPath), { recursive: true });
writeFileSync(outputPath, `${JSON.stringify(vehicles, null, 2)}\n`);
console.log(JSON.stringify(summarize(vehicles), null, 2));
