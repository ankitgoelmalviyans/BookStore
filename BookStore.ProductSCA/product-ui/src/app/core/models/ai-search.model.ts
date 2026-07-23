// Mirrors AiService's BookMatch/BookSearchResult. Unlike recommendations, matches already carry
// denormalised name/description/price — AiService's own BookEmbeddings index stores that data
// itself, so no client-side join against ProductService is needed here.
export interface BookMatch {
  productId: string;
  name: string;
  description: string;
  price: number;
  score: number;
}

export interface BookSearchResult {
  answer: string;
  matches: BookMatch[];
}
