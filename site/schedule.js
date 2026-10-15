// Shared by the frame page (WebView 106) and by tools/build-site.mjs (Node).
// Plain ES2020, no dependencies.

export const SCREEN_TYPES = ['clock', 'departures', 'url'];
export const MAX_DEPARTURE_LIMIT = 20;
export const TIME_ZONE = 'Europe/Warsaw';

export const DAYS = { sun: 0, mon: 1, tue: 2, wed: 3, thu: 4, fri: 5, sat: 6 };

const TIME_PATTERN = /^([01]\d|2[0-3]):[0-5]\d$/;
const FRAME_ID_PATTERN = /^[a-z0-9-]{1,40}$/;

// A misspelled key must fail the build, not silently do nothing on the frame.
const ALLOWED_KEYS = {
  root: ['screens', 'frames'],
  screen: ['type', 'title', 'stops', 'limit', 'url'],
  frame: ['default', 'schedule', 'dim'],
  rule: ['screen', 'from', 'to', 'days'],
  dim: ['from', 'to', 'days', 'level'],
};

export function isValidFrameId(frameId) {
  return typeof frameId === 'string' && FRAME_ID_PATTERN.test(frameId);
}

// screens.json is edited by hand: allow // and /* */ comments and trailing commas.
export function parseJsonc(text) {
  return JSON.parse(removeTrailingCommas(stripComments(text)));
}

function stripComments(text) {
  let out = '';
  let inString = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    const next = text[i + 1];
    if (inString) {
      out += c;
      if (c === '\\') {
        out += next;
        i++;
      } else if (c === '"') {
        inString = false;
      }
    } else if (c === '"') {
      inString = true;
      out += c;
    } else if (c === '/' && next === '/') {
      while (i < text.length && text[i] !== '\n') i++;
      out += '\n';
    } else if (c === '/' && next === '*') {
      i += 2;
      while (i < text.length && !(text[i] === '*' && text[i + 1] === '/')) i++;
      i++;
    } else {
      out += c;
    }
  }
  return out;
}

function removeTrailingCommas(text) {
  let out = '';
  let inString = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (inString) {
      out += c;
      if (c === '\\') {
        out += text[i + 1];
        i++;
      } else if (c === '"') {
        inString = false;
      }
      continue;
    }
    if (c === '"') inString = true;
    if (c === ',') {
      let j = i + 1;
      while (j < text.length && /\s/.test(text[j])) j++;
      if (text[j] === '}' || text[j] === ']') continue;
    }
    out += c;
  }
  return out;
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function unknownKeys(value, kind, label) {
  return Object.keys(value)
    .filter((key) => !ALLOWED_KEYS[kind].includes(key))
    .map((key) => `${label}: unknown key "${key}". Allowed: ${ALLOWED_KEYS[kind].join(', ')}.`);
}

export function validateConfig(config) {
  if (!isObject(config)) return ['The config must be a JSON object.'];

  const errors = unknownKeys(config, 'root', 'Config');
  const screens = isObject(config.screens) ? config.screens : null;
  const frames = isObject(config.frames) ? config.frames : null;

  if (!screens || Object.keys(screens).length === 0) errors.push('"screens" must be a non-empty object.');
  if (!frames || Object.keys(frames).length === 0) errors.push('"frames" must be a non-empty object, one entry per frame.');

  for (const [name, screen] of Object.entries(screens || {})) errors.push(...validateScreen(name, screen));
  for (const [frameId, plan] of Object.entries(frames || {})) errors.push(...validateFrame(frameId, plan, screens || {}));
  return errors;
}

function validateScreen(name, screen) {
  const label = `Screen "${name}"`;
  if (!isObject(screen)) return [`${label} must be an object.`];

  const errors = unknownKeys(screen, 'screen', label);
  if (!SCREEN_TYPES.includes(screen.type)) {
    errors.push(`${label}: unknown type "${screen.type}". Use one of: ${SCREEN_TYPES.join(', ')}.`);
    return errors;
  }
  if (screen.title !== undefined && typeof screen.title !== 'string') errors.push(`${label}: "title" must be text.`);

  if (screen.type === 'departures') {
    if (!Array.isArray(screen.stops) || screen.stops.length === 0) {
      errors.push(`${label}: "stops" needs at least one MZK busStopId.`);
    } else if (!screen.stops.every((id) => Number.isInteger(id) && id > 0)) {
      errors.push(`${label}: every stop id must be a positive whole number.`);
    }
    if (screen.limit !== undefined && !(Number.isInteger(screen.limit) && screen.limit >= 1 && screen.limit <= MAX_DEPARTURE_LIMIT)) {
      errors.push(`${label}: "limit" must be a whole number from 1 to ${MAX_DEPARTURE_LIMIT}.`);
    }
  }

  if (screen.type === 'url' && !isHttpsUrl(screen.url)) {
    errors.push(`${label}: "url" must be an absolute https address. The frame page is https, so an http iframe stays blank.`);
  }
  return errors;
}

function validateFrame(frameId, plan, screens) {
  const label = `Frame "${frameId}"`;
  if (!isValidFrameId(frameId)) return [`${label}: use only lowercase letters, digits and "-" in a frame id (max 40).`];
  if (!isObject(plan)) return [`${label} must be an object.`];

  const errors = unknownKeys(plan, 'frame', label);
  if (!Object.prototype.hasOwnProperty.call(screens, plan.default)) {
    errors.push(`${label}: "default" names screen "${plan.default}", which is not in "screens".`);
  }

  errors.push(...validateList(plan.schedule, `${label}, "schedule"`, (rule, ruleLabel) => {
    const ruleErrors = validateWindow(rule, 'rule', ruleLabel);
    if (isObject(rule) && !Object.prototype.hasOwnProperty.call(screens, rule.screen)) {
      ruleErrors.push(`${ruleLabel}: screen "${rule.screen}" is not in "screens".`);
    }
    return ruleErrors;
  }));

  errors.push(...validateList(plan.dim, `${label}, "dim"`, (rule, ruleLabel) => {
    const ruleErrors = validateWindow(rule, 'dim', ruleLabel);
    if (isObject(rule) && !(typeof rule.level === 'number' && rule.level > 0 && rule.level <= 1)) {
      ruleErrors.push(`${ruleLabel}: "level" must be a number above 0 and up to 1 (1 is fully black).`);
    }
    return ruleErrors;
  }));
  return errors;
}

function validateList(list, label, validateItem) {
  if (list === undefined) return [];
  if (!Array.isArray(list)) return [`${label} must be a list.`];
  return list.flatMap((item, index) => validateItem(item, `${label} rule ${index + 1}`));
}

function validateWindow(rule, kind, label) {
  if (!isObject(rule)) return [`${label} must be an object.`];

  const errors = unknownKeys(rule, kind, label);
  if (!TIME_PATTERN.test(rule.from) || !TIME_PATTERN.test(rule.to)) {
    errors.push(`${label}: "from" and "to" must be times like "06:30".`);
  } else if (rule.from === rule.to) {
    errors.push(`${label}: "from" and "to" are the same time.`);
  }

  if (rule.days !== undefined) {
    if (!Array.isArray(rule.days)) {
      errors.push(`${label}: "days" must be a list like ["mon", "fri"].`);
    } else {
      for (const day of rule.days) {
        if (!Object.prototype.hasOwnProperty.call(DAYS, day)) errors.push(`${label}: unknown day "${day}". Use mon, tue, wed, thu, fri, sat, sun.`);
      }
    }
  }
  return errors;
}

function isHttpsUrl(value) {
  try {
    return new URL(value).protocol === 'https:';
  } catch (error) {
    return false;
  }
}

// The wall-clock time in Europe/Warsaw, whatever time zone the frame itself is set to.
export function localNow(date, timeZone = TIME_ZONE) {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone,
    weekday: 'short',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(date);
  const get = (type) => parts.find((part) => part.type === type).value;
  return {
    day: DAYS[get('weekday').toLowerCase()],
    minutes: Number(get('hour')) * 60 + Number(get('minute')),
  };
}

function toMinutes(time) {
  return Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));
}

function runsOn(rule, day) {
  return !Array.isArray(rule.days) || rule.days.length === 0 || rule.days.some((name) => DAYS[name] === day);
}

// "days" names the day a window starts on, so a window past midnight checks yesterday after 00:00.
export function windowMatches(rule, now) {
  const from = toMinutes(rule.from);
  const to = toMinutes(rule.to);
  if (from < to) return now.minutes >= from && now.minutes < to && runsOn(rule, now.day);
  if (now.minutes >= from) return runsOn(rule, now.day);
  return now.minutes < to && runsOn(rule, (now.day + 6) % 7);
}

export function resolveScreenName(plan, now) {
  const rule = (plan.schedule || []).find((item) => windowMatches(item, now));
  return rule ? rule.screen : plan.default;
}

// 0 means no overlay.
export function resolveDimLevel(plan, now) {
  const rule = (plan.dim || []).find((item) => windowMatches(item, now));
  return rule ? rule.level : 0;
}
