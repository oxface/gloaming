import { check } from "k6";
import http from "k6/http";
import { Trend } from "k6/metrics";

type Fixture = {
  cookie: string;
  search: string;
  pageSize: number;
  deepOffset: number;
  visibleOrders: number;
};

type DraftPage = {
  totalCount: number;
  items: { code: string; total: number }[];
};

const fixture: Fixture = JSON.parse(open("/session/request.json"));
const firstPage = new Trend("draft_first_page_duration", true);
const deepPage = new Trend("draft_deep_page_duration", true);
const arrival = {
  executor: "constant-arrival-rate",
  rate: 4,
  timeUnit: "1s",
  duration: "20s",
  preAllocatedVUs: 2,
  maxVUs: 8,
  gracefulStop: "5s",
};

export const options = {
  scenarios: {
    first_page: { ...arrival, exec: "readFirstPage" },
    deep_page: { ...arrival, exec: "readDeepPage" },
  },
  summaryTrendStats: ["med", "p(95)", "p(99)"],
  thresholds: {
    checks: ["rate==1"],
    http_req_failed: ["rate==0"],
    dropped_iterations: ["count==0"],
    "iterations{scenario:first_page}": ["count>0"],
    "iterations{scenario:deep_page}": ["count>0"],
  },
};

function readPage(offset: number, timing: Trend) {
  const response = http.get(
    `${__ENV.WHOLESALE_ORIGIN}${fixture.search}&limit=${fixture.pageSize}&offset=${offset}`,
    { headers: { Cookie: fixture.cookie }, redirects: 0, timeout: "5s" },
  );
  timing.add(response.timings.duration);
  check(response, {
    "page contains the expected authorized orders and count": (result) => {
      if (result.status !== 200) return false;

      try {
        const page = result.json() as DraftPage | null;
        return (
          page?.totalCount === fixture.visibleOrders &&
          Array.isArray(page.items) &&
          page.items.length === fixture.pageSize &&
          page.items.every((order, index) => {
            const sequence = (offset + index + 1) * 2;
            return (
              order?.code ===
                `south-load-${String(sequence).padStart(4, "0")}` &&
              order.total === 10000 + sequence
            );
          })
        );
      } catch {
        return false;
      }
    },
  });
}

export function readFirstPage() {
  readPage(0, firstPage);
}

export function readDeepPage() {
  readPage(fixture.deepOffset, deepPage);
}

export function handleSummary(data: unknown) {
  return { "/reports/summary.json": JSON.stringify(data, null, 2) };
}
